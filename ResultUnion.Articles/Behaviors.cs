using ResultUnion;
using ResultUnion.AspNetCore;
using ResultUnion.Diagnostics;

namespace ResultUnion.Articles;

public interface IOrderRepository
{
    Task AddAsync(Order order, CancellationToken ct);
}

// ---- verbatim from pipeline-behaviors.md ----
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

public sealed class PlaceOrderValidator : ICommandValidator<PlaceOrder>
{
    public Result Validate(PlaceOrder command) =>
        command.Lines.Count > 0 ? Result.Success() : Error.Validation("At least one line is required.");
}

public static class BehaviorWiring
{
    public static void Register(WebApplicationBuilder builder)
    {
        // ---- verbatim ----
        builder.Services.AddScoped<PlaceOrderHandler>();
        builder.Services.AddScoped<ICommandValidator<PlaceOrder>, PlaceOrderValidator>();
        builder.Services.AddScoped<ICommandHandler<PlaceOrder, Result<Guid>>>(services =>
            new ValidationBehavior<PlaceOrder, Result<Guid>>(
                new TransactionBehavior<PlaceOrder, Result<Guid>>(
                    services.GetRequiredService<PlaceOrderHandler>(),
                    services.GetRequiredService<AppDbContext>()),
                services.GetServices<ICommandValidator<PlaceOrder>>()));
    }

    public static void Map(WebApplication app)
    {
        // ---- verbatim ----
        app.MapPost("/orders", (PlaceOrder command, ICommandHandler<PlaceOrder, Result<Guid>> handler, CancellationToken ct) =>
                handler.HandleAsync(command, ct)
                    .ToHttpResultAsync(id => TypedResults.Created($"/orders/{id}", id)))
            .ProducesErrors(ErrorType.Validation, ErrorType.NotFound);
    }
}
