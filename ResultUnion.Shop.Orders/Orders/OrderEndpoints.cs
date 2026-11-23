using ResultUnion.AspNetCore;

namespace ResultUnion.Shop.Orders.Orders;

public sealed record CancelOrderRequest(int Version);

public sealed record OrderLineResponse(string Product, int Quantity);

public sealed record OrderResponse(Guid Id, Guid CustomerId, IReadOnlyList<OrderLineResponse> Lines, string Status, DateTimeOffset PlacedAt, int Version)
{
    public static OrderResponse From(Order order) =>
        new(order.Id, order.CustomerId, [.. order.Lines.Select(line => new OrderLineResponse(line.Product, line.Quantity))],
            order.Status.ToString(), order.PlacedAt, order.Version);
}

public static class OrderEndpoints
{
    public static IEndpointRouteBuilder MapOrders(this IEndpointRouteBuilder app)
    {
        var orders = app.MapGroup("/orders").WithTags("Orders");

        // 201, or 400 (every invalid input), 404 (customer), 409 (out of stock), 503 (inventory unreachable).
        orders.MapPost("/", (PlaceOrder command, OrderService service, CancellationToken ct) =>
                service.PlaceAsync(command, ct)
                    .MapAsync(OrderResponse.From)
                    .ToHttpResultAsync(order => TypedResults.Created($"/orders/{order.Id}", order)))
            .ProducesErrors(ErrorType.Validation, ErrorType.NotFound, ErrorType.Conflict, ErrorType.Unavailable);

        orders.MapGet("/{id:guid}", (Guid id, OrderService service, CancellationToken ct) =>
                service.FindAsync(id, ct).MapAsync(OrderResponse.From).ToHttpResultAsync())
            .ProducesErrors(ErrorType.NotFound);

        orders.MapPost("/{id:guid}/cancel", (Guid id, CancelOrderRequest request, OrderService service, CancellationToken ct) =>
                service.CancelAsync(id, request.Version, ct)
                    .MapAsync(OrderResponse.From)
                    .ToHttpResultAsync())
            .ProducesErrors(ErrorType.NotFound, ErrorType.BusinessRule, ErrorType.Concurrency, ErrorType.Unavailable);

        return app;
    }
}
