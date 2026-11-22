# Pipeline behaviors

Validation, authorization, logging and transactions apply to many use cases. Written once as a *behavior* that wraps
every handler, they stay out of the use cases themselves. The difficulty is the return type: one handler returns
`Result`, another `Result<OrderId>`, another `Result<UserDto>`, and a behavior that rejects a request must return a
failure of whichever type the handler has. `IResultFactory<TSelf>` solves that.

## The handler shape

Use cases implement one generic interface. The constraint on `TResponse` is the only ResultUnion-specific part:

```csharp
public interface ICommandHandler<in TCommand, TResponse>
    where TResponse : IResultFactory<TResponse>
{
    Task<TResponse> HandleAsync(TCommand command, CancellationToken ct);
}

public sealed record PlaceOrder(Guid CustomerId, IReadOnlyList<OrderLineInput> Lines);

public sealed class PlaceOrderHandler(IOrderRepository orders) : ICommandHandler<PlaceOrder, Result<Guid>>
{
    public Task<Result<Guid>> HandleAsync(PlaceOrder command, CancellationToken ct) =>
        command.Lines.CombineAll(line => OrderLine.Create(line.Product, line.Quantity), field: "lines")
            .Bind(lines => Order.Create(command.CustomerId, lines))
            .TapAsync(order => orders.AddAsync(order, ct))
            .MapAsync(order => order.Id);
}
```

`Result` and every `Result<T>` implement `IResultFactory<TSelf>`, which has one member, `static TSelf Failure(Error)`,
and extends `IAnyResult` (`IsSuccess`, `IsFailure`, `Error`). So code that knows the response only as `TResponse`
can create a failure and read the outcome. There is no generic `Success`: a behavior never has the value a successful
`Result<Guid>` would need, and it does not need one, because success always comes from the handler.

## A validation behavior

Validators return a `Result`, so they compose with `CombineAll` like any other check. The behavior runs every
validator for the command, and only calls the handler when all of them pass:

```csharp
public interface ICommandValidator<in TCommand>
{
    Result Validate(TCommand command);
}

public sealed class ValidationBehavior<TCommand, TResponse>(
    ICommandHandler<TCommand, TResponse> inner,
    IEnumerable<ICommandValidator<TCommand>> validators)
    : ICommandHandler<TCommand, TResponse>
    where TResponse : IResultFactory<TResponse>
{
    public Task<TResponse> HandleAsync(TCommand command, CancellationToken ct) =>
        Result.CombineAll([.. validators.Select(v => v.Validate(command))]).TryGetError(out var error)
            ? Task.FromResult(TResponse.Failure(error))
            : inner.HandleAsync(command, ct);
}
```

A failed validation returns `TResponse.Failure(error)`: a `Result<Guid>` for `PlaceOrder`, a `Result` for a command
without a value, without the behavior knowing which.

## A transaction behavior

A behavior can also look at the handler's outcome. This one commits only when the use case succeeded, so a failure
anywhere in the pipeline leaves the database unchanged:

```csharp
public sealed class TransactionBehavior<TCommand, TResponse>(
    ICommandHandler<TCommand, TResponse> inner, AppDbContext db)
    : ICommandHandler<TCommand, TResponse>
    where TResponse : IResultFactory<TResponse>
{
    public async Task<TResponse> HandleAsync(TCommand command, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var response = await inner.HandleAsync(command, ct);
        if (response.IsSuccess)
            await transaction.CommitAsync(ct);

        return response;
    }
}
```

An exception from the handler is not caught: disposing the transaction rolls it back, and the exception continues to
your error handling. Failures and exceptions stay separate, as everywhere in ResultUnion.

## Wiring the behaviors

A behavior only works if calls go through it. Without a mediator library, register each handler wrapped in its
behaviors; the outermost one is resolved by the endpoint:

```csharp
builder.Services.AddScoped<PlaceOrderHandler>();
builder.Services.AddScoped<ICommandValidator<PlaceOrder>, PlaceOrderValidator>();
builder.Services.AddScoped<ICommandHandler<PlaceOrder, Result<Guid>>>(services =>
    new ValidationBehavior<PlaceOrder, Result<Guid>>(
        new TransactionBehavior<PlaceOrder, Result<Guid>>(
            services.GetRequiredService<PlaceOrderHandler>(),
            services.GetRequiredService<AppDbContext>()),
        services.GetServices<ICommandValidator<PlaceOrder>>()));
```

```csharp
app.MapPost("/orders", (PlaceOrder command, ICommandHandler<PlaceOrder, Result<Guid>> handler, CancellationToken ct) =>
        handler.HandleAsync(command, ct)
            .ToHttpResultAsync(id => TypedResults.Created($"/orders/{id}", id)))
    .ProducesErrors(ErrorType.Validation, ErrorType.NotFound);
```

With a mediator library the same constraint goes on its behavior interface. In MediatR, for example, an
`IPipelineBehavior<TRequest, TResponse>` with `where TResponse : IResultFactory<TResponse>` returns
`TResponse.Failure(error)` instead of calling `next()`. The ResultUnion part is identical.

## Recording failures

A behavior is also a good place to count failures in OpenTelemetry for every use case at once. `ResultTelemetry.Record`
takes the error and an operation name; the command's type name is stable and low-cardinality, which is what a metric
attribute needs:

```csharp
using ResultUnion.Diagnostics;

public sealed class TelemetryBehavior<TCommand, TResponse>(ICommandHandler<TCommand, TResponse> inner)
    : ICommandHandler<TCommand, TResponse>
    where TResponse : IResultFactory<TResponse>
{
    public async Task<TResponse> HandleAsync(TCommand command, CancellationToken ct)
    {
        var response = await inner.HandleAsync(command, ct);
        if (response.IsFailure)
            ResultTelemetry.Record(response.Error, typeof(TCommand).Name);

        return response;
    }
}
```

Without behaviors, end each handler's pipeline with `.RecordAsync("orders.place")` instead. Enable the meter with
`AddMeter("ResultUnion")` in your OpenTelemetry setup. Failures that reach HTTP or gRPC are also tagged on the
request's span by the integrations, without any code.
