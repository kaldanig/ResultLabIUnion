namespace ResultUnionApi.Products;

/// <summary>The name of a product: required, trimmed, at most <see cref="MaxLength"/> characters.</summary>
public sealed record ProductName
{
    public const int MaxLength = 100;

    /// <summary>
    /// The errors a name can have. Codes are a contract with clients, who switch on them and translate them, so each
    /// one is declared once, here. The request field is not set: the caller knows what the request calls it.
    /// </summary>
    public static class Errors
    {
        public static readonly Error Required =
            Error.Create("product_name.required", "The product name is required.", ErrorType.Validation);

        public static Error TooLong(int length) =>
            Error.Create("product_name.too_long", $"The product name has {length} characters; at most {MaxLength} are allowed.", ErrorType.Validation)
                .WithArgument("length", length)
                .WithArgument("maxLength", MaxLength);
    }

    public string Value { get; }

    private ProductName(string value) => Value = value;

    public static Result<ProductName> Create(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return Errors.Required;                                  // an Error converts to Result<ProductName>

        var trimmed = input.Trim();
        return trimmed.Length <= MaxLength
            ? new ProductName(trimmed)                               // so does a ProductName
            : Errors.TooLong(trimmed.Length);
    }

    public override string ToString() => Value;
}
