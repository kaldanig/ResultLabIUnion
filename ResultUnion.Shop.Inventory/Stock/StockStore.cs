namespace ResultUnion.Shop.Inventory.Stock;

/// <summary>
/// The stock, in memory, seeded at startup. A reservation is all or nothing: every line is checked under one lock
/// before any stock is taken, so two orders cannot both get the last items.
/// </summary>
public sealed class StockStore
{
    private readonly Dictionary<string, int> _available = new(StringComparer.Ordinal)
    {
        ["APPLE"] = 100,
        ["PEAR"] = 50,
        ["PLUM"] = 5,
    };

    private readonly Dictionary<Guid, Reservation> _reservations = [];
    private readonly Lock _gate = new();

    public int? Available(Sku sku)
    {
        lock (_gate)
            return _available.TryGetValue(sku.Value, out var count) ? count : null;
    }

    public Result Reserve(Reservation reservation)
    {
        lock (_gate)
        {
            if (_reservations.ContainsKey(reservation.OrderId))
                return Reservation.Errors.AlreadyExists(reservation.OrderId);

            // The same SKU on two lines counts once, with both quantities.
            foreach (var (sku, requested) in Totals(reservation))
            {
                if (!_available.TryGetValue(sku.Value, out var available))
                    return StockErrors.UnknownSku(sku);
                if (available < requested)
                    return StockErrors.Insufficient(sku, requested, available);
            }

            foreach (var (sku, requested) in Totals(reservation))
                _available[sku.Value] -= requested;

            _reservations[reservation.OrderId] = reservation;
            return Result.Success();
        }
    }

    public Result Release(Guid orderId)
    {
        lock (_gate)
        {
            if (!_reservations.Remove(orderId, out var reservation))
                return Reservation.Errors.NotFound(orderId);

            foreach (var (sku, quantity) in Totals(reservation))
                _available[sku.Value] += quantity;

            return Result.Success();
        }
    }

    private static IEnumerable<(Sku Sku, int Quantity)> Totals(Reservation reservation) =>
        reservation.Lines.GroupBy(line => line.Sku).Select(group => (group.Key, group.Sum(line => line.Quantity)));
}
