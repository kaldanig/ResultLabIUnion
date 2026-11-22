using System.Text.RegularExpressions;

namespace ResultUnion.Shop.Inventory.Stock;

/// <summary>A stock keeping unit: 3 to 20 letters, digits or dashes, stored in upper case.</summary>
public sealed partial record Sku
{
    public static class Errors
    {
        public static readonly Error Required =
            Error.Create("sku.required", "The SKU is required.", ErrorType.Validation);

        public static Error InvalidFormat(string value) =>
            Error.Create("sku.invalid_format", $"'{value}' is not a SKU: 3 to 20 letters, digits or dashes.", ErrorType.Validation)
                .WithArgument("value", value);
    }

    public string Value { get; }

    private Sku(string value) => Value = value;

    public static Result<Sku> Create(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return Errors.Required;

        var normalized = input.Trim().ToUpperInvariant();
        return Format().IsMatch(normalized) ? new Sku(normalized) : Errors.InvalidFormat(input.Trim());
    }

    public override string ToString() => Value;

    [GeneratedRegex("^[A-Z0-9-]{3,20}$")]
    private static partial Regex Format();
}
