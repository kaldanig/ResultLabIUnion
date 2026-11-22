using ResultUnion.AspNetCore;

namespace ResultUnion.Shop.Inventory.Stock;

public sealed record StockResponse(string Sku, int Available);

public sealed record ReservationResponse(Guid OrderId, IReadOnlyList<ReserveStockLine> Lines)
{
    public static ReservationResponse From(Reservation reservation) =>
        new(reservation.OrderId, [.. reservation.Lines.Select(line => new ReserveStockLine(line.Sku.Value, line.Quantity))]);
}

public static class StockEndpoints
{
    public static IEndpointRouteBuilder MapStock(this IEndpointRouteBuilder app)
    {
        app.MapGet("/stock/{sku}", (string sku, StockService service) =>
                service.Available(sku)
                    .Map(available => new StockResponse(sku.Trim().ToUpperInvariant(), available))
                    .ToHttpResult())
            .WithTags("Stock")
            .ProducesErrors(ErrorType.Validation, ErrorType.NotFound);

        var reservations = app.MapGroup("/reservations").WithTags("Reservations");

        reservations.MapPost("/", (ReserveStock command, StockService service) =>
                service.Reserve(command)
                    .Map(ReservationResponse.From)
                    .ToHttpResult(reservation => TypedResults.Created($"/reservations/{reservation.OrderId}", reservation)))
            .ProducesErrors(ErrorType.Validation, ErrorType.NotFound, ErrorType.Conflict);

        reservations.MapDelete("/{orderId:guid}", (Guid orderId, StockService service) =>
                service.Release(orderId).ToHttpResult())
            .ProducesErrors(ErrorType.NotFound);

        return app;
    }
}
