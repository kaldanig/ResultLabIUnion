namespace ResultUnion.Shop.Inventory.Stock;

public sealed record ReserveStock(Guid OrderId, IReadOnlyList<ReserveStockLine>? Lines);

public sealed record ReserveStockLine(string? Sku, int Quantity);

/// <summary>The use cases of the stock service.</summary>
public sealed class StockService(StockStore store)
{
    public Result<int> Available(string? sku) =>
        Sku.Create(sku)
            .MapError(e => e.WithField("sku"))
            .Bind(valid => Result.FromNullable(store.Available(valid), StockErrors.UnknownSku(valid)))
            .Record("stock.available");

    public Result<Reservation> Reserve(ReserveStock command) =>
        (command.Lines ?? [])
            .CombineAll(line => ReservationLine.Create(line.Sku, line.Quantity), field: "lines")   // lines[1].quantity
            .Ensure(lines => lines.Count > 0, Reservation.Errors.NoLines.WithField("lines"))
            .Map(lines => new Reservation(command.OrderId, lines))
            .Ensure(reservation => store.Reserve(reservation))                                     // the store's answer
            .Record("stock.reserve");

    public Result Release(Guid orderId) =>
        store.Release(orderId).Record("stock.release");
}
