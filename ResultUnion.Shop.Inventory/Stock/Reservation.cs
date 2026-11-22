namespace ResultUnion.Shop.Inventory.Stock;

/// <summary>One line of a reservation: a SKU and how many. It names its own fields, since it has two.</summary>
public sealed record ReservationLine(Sku Sku, int Quantity)
{
    public const int MaxQuantity = 100;

    public static class Errors
    {
        public static Error QuantityOutOfRange(int quantity) =>
            Error.Create("reservation_line.quantity_out_of_range", $"The quantity must be from 1 to {MaxQuantity}; {quantity} is not.", ErrorType.Validation)
                .WithArgument("quantity", quantity)
                .WithArgument("max", MaxQuantity)
                .WithField("quantity");
    }

    public static Result<ReservationLine> Create(string? sku, int quantity) =>
        Result.CombineAll(
                Stock.Sku.Create(sku).MapError(e => e.WithField("sku")),
                quantity is >= 1 and <= MaxQuantity ? Result<int>.Success(quantity) : Errors.QuantityOutOfRange(quantity))
            .Map((validSku, validQuantity) => new ReservationLine(validSku, validQuantity));
}

/// <summary>The stock held for one order, until the order is shipped or cancelled.</summary>
public sealed record Reservation(Guid OrderId, IReadOnlyList<ReservationLine> Lines)
{
    public static class Errors
    {
        public static readonly Error NoLines =
            Error.Create("reservation.no_lines", "A reservation needs at least one line.", ErrorType.Validation);

        public static Error AlreadyExists(Guid orderId) =>
            Error.Create("reservation.already_exists", $"Order {orderId} already has a reservation.", ErrorType.Conflict)
                .WithArgument("orderId", orderId);

        public static Error NotFound(Guid orderId) =>
            Error.Create("reservation.not_found", $"Order {orderId} has no reservation.", ErrorType.NotFound)
                .WithArgument("orderId", orderId);
    }
}

/// <summary>The errors of the stock itself, which only the store can tell.</summary>
public static class StockErrors
{
    public static Error UnknownSku(Sku sku) =>
        Error.Create("stock.unknown_sku", $"There is no stock of {sku}.", ErrorType.NotFound)
            .WithArgument("sku", sku.Value);

    public static Error Insufficient(Sku sku, int requested, int available) =>
        Error.Create("stock.insufficient", $"Only {available} of {sku} are available; {requested} were requested.", ErrorType.Conflict)
            .WithArgument("sku", sku.Value)
            .WithArgument("requested", requested)
            .WithArgument("available", available);
}
