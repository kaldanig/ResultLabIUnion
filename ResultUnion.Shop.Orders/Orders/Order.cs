namespace ResultUnion.Shop.Orders.Orders;

/// <summary>
/// One line of an order. It has two inputs, so it names their fields itself (<c>product</c>, <c>quantity</c>); the
/// order adds the index (<c>lines[1].quantity</c>).
/// </summary>
public sealed record OrderLine(string Product, int Quantity)
{
    public const int MaxQuantity = 100;

    public static class Errors
    {
        public static readonly Error ProductRequired =
            Error.Create("order_line.product_required", "Each line needs a product.", ErrorType.Validation)
                .WithField("product");

        public static Error ProductInvalid(string value) =>
            Error.Create("order_line.product_invalid", $"'{value}' is not a product code: 3 to 20 letters, digits or dashes.", ErrorType.Validation)
                .WithArgument("value", value)
                .WithField("product");

        public static Error QuantityOutOfRange(int quantity) =>
            Error.Create("order_line.quantity_out_of_range", $"The quantity must be from 1 to {MaxQuantity}; {quantity} is not.", ErrorType.Validation)
                .WithArgument("quantity", quantity)
                .WithArgument("max", MaxQuantity)
                .WithField("quantity");
    }

    public static Result<OrderLine> Create(string? product, int quantity) =>
        Result.CombineAll(ProductCode(product), Count(quantity)).Map((code, count) => new OrderLine(code, count));

    // The same rule as the inventory's SKU, checked here too so that an order with a malformed code never leaves.
    private static Result<string> ProductCode(string? input)
    {
        var code = input?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(code))
            return Errors.ProductRequired;

        return code.Length is >= 3 and <= 20 && code.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')
            ? code
            : Errors.ProductInvalid(input!.Trim());
    }

    private static Result<int> Count(int quantity) =>
        quantity is >= 1 and <= MaxQuantity ? quantity : Errors.QuantityOutOfRange(quantity);
}

public enum OrderStatus
{
    Placed,
    Cancelled,
}

/// <summary>An order. <see cref="Version"/> is the concurrency token, as for customers.</summary>
public sealed class Order
{
    public static class Errors
    {
        public static readonly Error CustomerRequired =
            Error.Create("order.customer_required", "An order needs a customer.", ErrorType.Validation);

        public static readonly Error NoLines =
            Error.Create("order.no_lines", "An order needs at least one line.", ErrorType.Validation);

        public static Error NotFound(Guid id) =>
            Error.Create("order.not_found", $"There is no order {id}.", ErrorType.NotFound)
                .WithArgument("id", id);

        public static Error AlreadyCancelled(Guid id) =>
            Error.Create("order.already_cancelled", $"Order {id} is already cancelled.", ErrorType.BusinessRule)
                .WithArgument("id", id);

        public static Error ChangedConcurrently(Guid id, int version) =>
            Error.Create("order.changed_concurrently", $"Order {id} changed after version {version} was read.", ErrorType.Concurrency)
                .WithArgument("id", id)
                .WithArgument("version", version);

        public static Error UnknownProduct(string product) =>
            Error.Create("order.unknown_product", $"There is no product {product}.", ErrorType.Validation)
                .WithArgument("product", product);

        public static Error OutOfStock(string product, int requested, int available) =>
            Error.Create("order.out_of_stock", $"Only {available} of {product} are in stock; the order needs {requested}.", ErrorType.Conflict)
                .WithArgument("product", product)
                .WithArgument("requested", requested)
                .WithArgument("available", available);
    }

    public Guid Id { get; private set; }
    public Guid CustomerId { get; private set; }
    public IReadOnlyList<OrderLine> Lines { get; private set; } = [];
    public OrderStatus Status { get; private set; }
    public DateTimeOffset PlacedAt { get; private set; }
    public int Version { get; private set; }

    private Order()
    {
    }

    public static Order Place(Guid customerId, IReadOnlyList<OrderLine> lines, DateTimeOffset now) =>
        new() { Id = Guid.NewGuid(), CustomerId = customerId, Lines = lines, Status = OrderStatus.Placed, PlacedAt = now, Version = 1 };

    public Result Cancel()
    {
        if (Status == OrderStatus.Cancelled)
            return Errors.AlreadyCancelled(Id);

        Status = OrderStatus.Cancelled;
        Version++;
        return Result.Success();
    }
}
