# EF Core

Entity Framework Core reports "not found" as `null` and conflicts as exceptions. This page shows where to turn each of
them into a result, so the use case above the repository sees only `Result<T>`. The examples use a `Order` aggregate
with an error catalog like the one in [Getting started](getting-started.md).

## Lookups: `null` becomes a failure

`ToResultAsync(error)` awaits a lookup and turns `null` into the given failure. It works on the `Task<T?>` of
`FirstOrDefaultAsync` and on the `ValueTask<T?>` of `FindAsync`:

```csharp
public Task<Result<Order>> GetAsync(Guid id, CancellationToken ct) =>
    db.Orders.FindAsync([id], ct).ToResultAsync(Order.Errors.NotFound(id));

public Task<Result<Order>> GetByNumberAsync(string number, CancellationToken ct) =>
    db.Orders.Include(o => o.Lines)
        .FirstOrDefaultAsync(o => o.Number == number, ct)
        .ToResultAsync(Order.Errors.NumberNotFound(number));
```

Only `null` becomes an error. An exception from the query itself (the database is down, a bad mapping) is not caught:
it is not something the caller can fix, so it stays an exception and reaches your error handling middleware.

## Saving: translate the exceptions you expect

`SaveChangesAsync` throws `DbUpdateConcurrencyException` when a row changed since it was loaded (with a concurrency
token configured). That is an expected outcome: the user should reload and try again. `Result.TryAsync` with a
**typed** mapper translates that one exception and lets every other exception propagate:

```csharp
public Task<Result> SaveAsync(Order order, CancellationToken ct) =>
    Result.TryAsync(
        async () => { await db.SaveChangesAsync(ct); },
        (DbUpdateConcurrencyException _) => Order.Errors.ChangedConcurrently(order.Id));
```

`ErrorType.Concurrency` maps to `409 Conflict` over HTTP and `Aborted` over gRPC, and `error.IsTransient` is true for
it, so a retry policy may retry it.

The lambda has a block body (`async () => { await …; }`) because the order needs no value back; `TryAsync` then returns
`Task<Result>`. `TryAsync` never catches `OperationCanceledException`: a cancelled request is not a business failure.

## Unique constraints: check first, catch the race

A unique index (one account per email) needs two things. A check before the insert gives a clear error in the common
case. The index still decides when two requests insert the same email at the same moment, and the loser gets a
`DbUpdateException`, so that case needs a filter on the provider's error:

```csharp
public async Task<Result> AddAsync(User user, CancellationToken ct)
{
    if (await db.Users.AnyAsync(u => u.Email == user.Email, ct))
        return User.Errors.EmailTaken(user.Email);

    db.Users.Add(user);
    try
    {
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
    catch (DbUpdateException ex) when (IsUniqueViolation(ex))
    {
        db.Entry(user).State = EntityState.Detached;
        return User.Errors.EmailTaken(user.Email);
    }
}
```

This is one of the few places where a hand-written `catch` is right: `TryAsync` translates an exception *type*, and
a unique violation is a `DbUpdateException` like many others, recognisable only by the provider's error number.
`IsUniqueViolation` checks it (for SQL Server, `SqlException.Number` 2601 or 2627; for PostgreSQL, `PostgresException`
with `SqlState` 23505). Every other `DbUpdateException` still propagates.

## Use cases on top

With the repository returning results, a use case is one pipeline: load, apply the domain rule, save.

```csharp
public Task<Result<Order>> CancelAsync(Guid orderId, string? reason, CancellationToken ct) =>
    CancellationReason.Create(reason)
        .MapError(e => e.WithField("reason"))
        .BindAsync(validReason => orders.GetAsync(orderId, ct)
            .BindAsync(order => order.Cancel(validReason)))
        .EnsureAsync(order => orders.SaveAsync(order, ct));
```

- The input is validated before anything is loaded.
- `order.Cancel` returns a `Result<Order>`: the domain rule ("a shipped order cannot be cancelled") is a failure in
  the aggregate's catalog, not an exception.
- `EnsureAsync` with `SaveAsync` keeps the order and stops on a concurrency failure.

## What not to do

- **Do not turn every exception into `ErrorType.Unexpected`.** A failure the caller cannot fix is a bug or an outage;
  as an exception it keeps its stack trace and reaches logging and monitoring. An untyped mapper (`ex => …`) in
  `TryAsync` is for code whose every exception really is an expected outcome.
- **Do not store a `Result`.** Map it to the entity or a DTO first; a result is not a persistence or JSON contract.
