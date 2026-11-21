namespace ResultUnion.Showcase.Domain;

/// <summary>The input for one order line, as it arrives from a client.</summary>
public sealed record OrderLineInput(string? Product, int Quantity);

/// <summary>A value object with two fields: it reports every invalid field, each named after its own property.</summary>
public sealed record OrderLine
{
    public static class Errors
    {
        public static readonly Error ProductRequired =
            Error.Create("order_line.product_required", "Product is required.", ErrorType.Validation);

        public static Error QuantityNotPositive(int quantity) =>
            Error.Create("order_line.quantity_not_positive", $"Quantity must be positive, was {quantity}.", ErrorType.Validation)
                .WithArgument("quantity", quantity);
    }

    public string Product { get; }

    public int Quantity { get; }

    private OrderLine(string product, int quantity) => (Product, Quantity) = (product, quantity);

    // The line knows its own field names; the collection adds the index in front: lines[1].quantity.
    public static Result<OrderLine> Create(OrderLineInput input) =>
        Result.CombineAll(
                string.IsNullOrWhiteSpace(input.Product)
                    ? Result<string>.Failure(Errors.ProductRequired.WithField("product"))
                    : input.Product.Trim(),
                input.Quantity > 0
                    ? Result<int>.Success(input.Quantity)
                    : Errors.QuantityNotPositive(input.Quantity).WithField("quantity"))
            .Map((product, quantity) => new OrderLine(product, quantity));

    public override string ToString() => $"{Quantity} x {Product}";
}
