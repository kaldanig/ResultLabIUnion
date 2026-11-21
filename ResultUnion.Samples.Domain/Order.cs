namespace ResultUnion.Samples.Domain;

/// <summary>An aggregate built from value objects: it can only be created with a customer and at least one valid line.</summary>
public sealed record Order
{
    public static class Errors
    {
        public static readonly Error NoLines =
            Error.Create("order.no_lines", "An order needs at least one line.", ErrorType.Validation);
    }

    public Guid Id { get; }

    public Email Customer { get; }

    public IReadOnlyList<OrderLine> Lines { get; }

    public int TotalQuantity => Lines.Sum(line => line.Quantity);

    private Order(Guid id, Email customer, IReadOnlyList<OrderLine> lines) => (Id, Customer, Lines) = (id, customer, lines);

    // The order names its own field, like OrderLine does for product and quantity.
    public static Result<Order> Create(Email customer, IReadOnlyList<OrderLine> lines) =>
        lines.Count == 0
            ? Errors.NoLines.WithField("lines")
            : new Order(Guid.NewGuid(), customer, lines);
}
