---
name: resultunion
description: How to write application code with the ResultUnion library (Result of T, Result, Error, ErrorType) — value objects with error catalogs, command handlers as Map/Bind/Ensure pipelines, async chains, CombineAll validation, Minimal API endpoints with ToHttpResult/ProducesErrors, gRPC services with GetValueOrThrowRpcAsync, and turning exceptions or nulls into results. Use this skill whenever you write, change or review C# code in a project that references ResultUnion, ResultUnion.AspNetCore or ResultUnion.Grpc, or whenever the user asks for a handler, endpoint, value object, validation or error handling "with Result", "returning a Result", "Result pattern" or "ResultUnion" — even if they don't name the library and only mention Error, ErrorType or a Result type.
---

# Writing code with ResultUnion

ResultUnion is a Result pattern for .NET built on C# 15 unions: an operation returns `Result<T>` (a value or an
`Error`) or `Result` (success or an `Error`). Failures are part of the signature, and a pipeline runs only while every
step succeeds. This skill is about *using* the library in an application. The library's own reasoning is in
`docs/DESIGN.md`, the full API in `README.md` (both in the ResultUnion repository).

The library differs from other Result libraries in a few names and rules. Most mistakes come from writing generic
"Result pattern" code from memory, so follow the conventions below even when another style looks familiar.

## Rules, with the reason for each

**Use a `Result` for expected failures, an exception for bugs.** Invalid input, a business rule, not found, conflict,
rate limit: `Result`. A `null` argument, an impossible state: throw (`ArgumentNullException.ThrowIfNull`). If the user
can fix it or should be told, it's a result; if a developer has to fix it, it's an exception.

**Chain with `Bind`, transform with `Map`.** There is no `Then`. `Map` takes `T → U`; `Bind` takes `T → Result<U>`
(or `T → Result`). `Ensure(predicate, error)` turns a success into a failure; `Ensure(v => resultReturningCheck(v))`
reuses a validator and keeps the value. `Tap` / `TapError` for side effects, `MapError` to translate an error,
`Match` at the end.

**Async: `Task`, never `ValueTask`.** Every operation has an `…Async` form, on `Result<T>` (async delegate) and on
`Task<Result<T>>` (sync or async delegate), so one chain needs a single `await` at the end. A `ValueTask` source needs
`.AsTask()`. Pass the `CancellationToken` by closure: `user => repo.AddAsync(user, ct)`; delegates take no token.

**Errors come from catalogs, not inline strings.** Each domain concept declares its errors in a nested `Errors`
class with stable codes `<concept>.<case>` (snake_case). Codes are a contract with clients (they switch on them and
translate them), so they must not be scattered or invented per call site. Fixed errors are `static readonly`; errors
that carry values are methods that add them with `WithArgument`, so clients can build a translated message.
Inline `Error.Validation("…")` is fine only for one-off generic cases with no domain meaning.

**Whoever knows the field name sets `Field`.** A value object that *is* one field (`Email`) does not call
`WithField`; the caller does: `Email.Create(cmd.Email).MapError(e => e.WithField("email"))`. A value object with several
fields (`OrderLine`) names its own (`product`, `quantity`), and collections add the index (`lines[2].quantity`).
So a catalog entry never calls `WithField` for a request field: the domain does not know what the request calls it.

**An input the aggregate receives becomes a value object, even a plain string with a length limit.** A `reason`
with "required, at most 200" is a `CancellationReason` with its own catalog, not checks inside
`Order.Cancel(string?)`. The aggregate's method then takes validated types and holds only the domain rule, and the
handler validates all inputs with `CombineAll` before it loads anything. The parts of a multi-field value object
(`OrderLine`'s sku and quantity) can stay private checks inside its `Create` unless other code needs them on their own.

**Validate every field at once.** `Result.CombineAll(a, b, c)` reports every failure (one failure comes back
unchanged, several are grouped in `Details`). With two to four `Result<T>` it returns a tuple, and `Map` / `Bind` on the
tuple take one parameter per value, so a factory fits as a method group: `.Bind(User.Create)`. For lists:
`items.CombineAll(OrderLine.Create, field: "lines")`. Use `Combine` only when stopping at the first failure is wanted.

**Read values safely.** `switch` on the union (`User user => …, Error error => …`), `Match`, or
`TryGetResultValue(out var v)` / `TryGetError(out var e)`. Do not write `TryGetValue(out var …)`: the union has two
`TryGetValue` overloads and `out var` is ambiguous (CS0121). `ResultValue` and `Error` throw on the wrong track, so
don't pair them with an `IsSuccess` check either: `if (result.TryGetError(out var error) && error.IsTransient)` checks
and reads in one step. An argument is read from the dictionary:
`error.Arguments.TryGetValue("retryAfterSeconds", out var value) && value is int seconds`.

**Exceptions and nulls at the boundary.** `Result.FromNullable(value, error)` and
`Task<T?>.ToResultAsync(error)` for lookups that return `null`. `Result.Try` / `Result.TryAsync` for throwing code:
type the mapper's parameter to translate one exception only — `(DbUpdateConcurrencyException ex) => Error.Concurrency(…)`;
an untyped `ex => …` catches everything except cancellation. Never pass an `async` lambda to `Result.Try` (exceptions
after the first `await` escape): use `TryAsync`. For no value, give `TryAsync` a block lambda: `async () => { await …; }`.

**Never serialize a `Result`.** Map it at the boundary: `ToHttpResult` for HTTP, `GetValueOrThrowRpcAsync` for gRPC,
or a DTO. `Result` and `Result<T>` are not JSON contracts.

**Let generic behaviors use `IResultFactory<TSelf>`.** A pipeline behavior that wraps handlers returning any result type
constrains `where TResponse : IResultFactory<TResponse>` and short-circuits with `TResponse.Failure(error)`.

## Template: value object with an error catalog

```csharp
public sealed record Email
{
    public static class Errors
    {
        public static readonly Error Required =
            Error.Create("email.required", "Email is required.", ErrorType.Validation);

        public static Error InvalidFormat(string value) =>
            Error.Create("email.invalid_format", $"'{value}' is not a valid email.", ErrorType.Validation)
                .WithArgument("value", value);
    }

    public string Value { get; }

    private Email(string value) => Value = value;

    public static Result<Email> Create(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return Errors.Required;                          // Error converts to Result<Email>

        var trimmed = input.Trim();
        return trimmed.Contains('@')
            ? new Email(trimmed.ToLowerInvariant())           // Email converts to Result<Email>
            : Errors.InvalidFormat(trimmed);
    }
}
```

`ErrorType` says what the caller should do, the code says why: Validation (fix the input), Unauthorized, Forbidden,
NotFound, Conflict (state prevents it), Concurrency (reload and retry), BusinessRule (a domain rule), TooManyRequests
(wait; add `retryAfterSeconds`), Unavailable (retry later), Unexpected (a bug). `error.IsTransient` is true for
Unavailable, TooManyRequests and Concurrency.

## Template: command handler as one pipeline

```csharp
public sealed record RegisterUser(string? Email, string? Password, string? Age);   // raw input from any host

public sealed class UserService(IUserRepository users, IUnitOfWork unitOfWork, ILogger<UserService> logger)
{
    public Task<Result<User>> RegisterAsync(RegisterUser command, CancellationToken ct) =>
        Result.CombineAll(
                Email.Create(command.Email).MapError(e => e.WithField("email")),
                Password.Create(command.Password).MapError(e => e.WithField("password")),
                User.ParseAge(command.Age).MapError(e => e.WithField("age")))
            .Bind((email, password, age) => User.Create(email, password, age))      // Result<User>
            .Ensure(user => user.IsAdult, User.Errors.TooYoung)                      // domain rule, catalog error
            .EnsureAsync(user => users.EnsureEmailIsFreeAsync(user.Email, ct))       // Task<Result> check, keeps the user
            .TapAsync(user => users.AddAsync(user, ct))
            .EnsureAsync(_ => unitOfWork.SaveChangesAsync(ct))                       // Task<Result>, keeps the user
            .TapErrorAsync(error => logger.LogInformation("Rejected: {Code}", error.Code));

    public Task<Result<User>> FindAsync(string? email, CancellationToken ct) =>
        Email.Create(email)
            .MapError(e => e.WithField("email"))
            .BindAsync(address => users.FindAsync(address, ct)                       // Task<User?>
                .ToResultAsync(User.Errors.NotFound(address)));
}
```

The handler takes a command, not an HTTP or gRPC type, so the same use case serves every host. Infrastructure that
returns generic errors (`Error.NotFound` from a repository) gets translated here with `MapError` into the domain's
catalog error.

## Template: adapter over an external API

An adapter turns every documented outcome of the other system into a catalog error, so callers can decide on
`ErrorType` alone: the user's problem (BusinessRule, Validation), wait and retry (TooManyRequests with the delay as an
argument, Unavailable), or a bug (Unexpected). "The provider is down" covers every way the call can fail to arrive:
a 503, a refused connection (`HttpRequestException`) and a timeout.

```csharp
public sealed class PaymentClient(HttpClient http)
{
    public static class Errors
    {
        public static readonly Error Declined =
            Error.Create("payment.declined", "The card was declined.", ErrorType.BusinessRule);
        public static Error RateLimited(int? retryAfterSeconds) =>
            Error.Create("payment.rate_limited", "Too many payment requests.", ErrorType.TooManyRequests)
                .WithArgument("retryAfterSeconds", retryAfterSeconds);
        public static readonly Error Unavailable =
            Error.Create("payment.unavailable", "The payment provider is not reachable.", ErrorType.Unavailable);
        public static Error UnexpectedResponse(int status) =>
            Error.Create("payment.unexpected_response", "The payment provider sent an unexpected response.", ErrorType.Unexpected)
                .WithArgument("status", status);
    }

    public async Task<Result<string>> ChargeAsync(Guid orderId, decimal amount, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            response = await http.PostAsJsonAsync("/charges", new { orderId, amount }, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException) { return Errors.Unavailable; }                         // connection failed
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { return Errors.Unavailable; } // HttpClient timeout

        using (response)
        {
            return response.StatusCode switch
            {
                HttpStatusCode.PaymentRequired => Errors.Declined,
                HttpStatusCode.TooManyRequests => Errors.RateLimited((int?)response.Headers.RetryAfter?.Delta?.TotalSeconds),
                HttpStatusCode.ServiceUnavailable => Errors.Unavailable,
                _ when response.IsSuccessStatusCode => await ReadTransactionIdAsync(response, ct).ConfigureAwait(false),
                var status => Errors.UnexpectedResponse((int)status),
            };
        }
    }
}
```

This is the one place a hand-written `catch` is right. HttpClient reports its own timeout as a
`TaskCanceledException`, and `Result.TryAsync` never catches cancellation, so tell the two apart with
`when (!ct.IsCancellationRequested)`; the caller's cancellation still propagates. For a single exception type that is
not cancellation, prefer `Result.TryAsync(call, (SomeException ex) => error)`. A retry loop then reads the error:
retry only when `error.IsTransient`, wait the `retryAfterSeconds` argument when present, pass `ct` to `Task.Delay`,
and never retry a BusinessRule or Unexpected error.

## Hosts

- **Minimal API endpoint** (`ResultUnion.AspNetCore`): read `references/http.md`. In short:
  `handler(...).MapAsync(Dto.From).ToHttpResultAsync(dto => TypedResults.Created(...))` plus
  `.ProducesErrors(ErrorType.Validation, ...)` on the endpoint.
- **gRPC service or client** (`ResultUnion.Grpc`): read `references/grpc.md`. In short: the service method ends with
  `.GetValueOrThrowRpcAsync()`; a client wraps calls in `Result.TryAsync(call, (RpcException ex) => ex.ToError())`.

## Quick check before you finish

Build the project: the package's analyzers report a discarded `Result` (RU0001), `async` code in `Result.Try`
(RU0002) and `Map` with a mapper that returns a `Result` (RU0003). Fix them rather than suppressing them; a discard
that is really intended is written `_ = ...`.

| If the code has… | Change it to… |
|---|---|
| `.Then(...)` | `.Bind(...)` (or `.Map(...)` if the lambda cannot fail) |
| `ValueTask<Result<…>>` | `Task<Result<…>>` |
| `TryGetValue(out var x)` | `TryGetResultValue(out var x)` / `TryGetError(out var e)` |
| `Error.Validation("Email is required.")` in a domain type | a catalog entry: `Email.Errors.Required` |
| `WithField("email")` inside `Email.Create` | `WithField` at the call site |
| `try { … } catch (Exception)` around a call | `Result.TryAsync(…, (SpecificException ex) => …)`; only an HttpClient timeout needs `catch (TaskCanceledException) when (!ct.IsCancellationRequested)` |
| `Error.Create(...)` inline in a handler or validator | a catalog entry on the concept the error is about |
| `.WithField("reason")` inside a catalog entry | the catalog without a field; `MapError(e => e.WithField("reason"))` in the handler |
| `if (!result.IsSuccess) … result.Error` | `if (result.TryGetError(out var error)) …` |
| an external call where only some failures become errors | every documented outcome (and timeouts, refused connections) mapped to a catalog error |
| `Result.Try(async () => …)` | `Result.TryAsync(async () => …)` |
| `if (x is null) return Error.NotFound(…)` after a lookup | `Result.FromNullable` / `.ToResultAsync(error)` |
| `result.ResultValue` without a check | `switch`, `Match` or `TryGetResultValue` |
| `return Ok(result)` / a `Result` in a DTO | `ToHttpResult` / a DTO mapped from the value |
| several `if` checks returning the first error | `Result.CombineAll(...)` |
| `catch (OperationCanceledException)` turned into an error | let cancellation propagate |
