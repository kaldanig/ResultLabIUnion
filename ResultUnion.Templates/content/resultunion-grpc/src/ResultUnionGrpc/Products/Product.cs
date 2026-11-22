namespace ResultUnionGrpc.Products;

/// <summary>A product. It is created from validated parts, so creating one cannot fail.</summary>
public sealed class Product
{
    public static class Errors
    {
        public static Error InvalidId(string value) =>
            Error.Create("product.invalid_id", $"'{value}' is not a product id.", ErrorType.Validation)
                .WithArgument("value", value);

        public static Error NotFound(Guid id) =>
            Error.Create("product.not_found", $"There is no product {id}.", ErrorType.NotFound)
                .WithArgument("id", id);

        public static Error NameTaken(ProductName name) =>
            Error.Create("product.name_taken", $"A product named '{name}' already exists.", ErrorType.Conflict)
                .WithArgument("name", name.Value);
    }

    public Guid Id { get; }
    public ProductName Name { get; }
    public Price Price { get; }

    private Product(Guid id, ProductName name, Price price)
    {
        Id = id;
        Name = name;
        Price = price;
    }

    public static Product Create(ProductName name, Price price) => new(Guid.NewGuid(), name, price);

    /// <summary>An id as a client sends it: protobuf has no GUID type, so it arrives as text.</summary>
    public static Result<Guid> ParseId(string? input) =>
        Guid.TryParse(input, out var id) ? id : Errors.InvalidId(input ?? string.Empty);
}
