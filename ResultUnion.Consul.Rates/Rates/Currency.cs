namespace ResultUnion.Consul.Rates.Rates;

/// <summary>An ISO 4217 code: three letters, kept upper-case. Whether there is a rate for it is the table's question.</summary>
public sealed record Currency
{
    public static class Errors
    {
        public static readonly Error Required =
            Error.Create("currency.required", "A currency is required.", ErrorType.Validation);

        public static Error InvalidFormat(string value) =>
            Error.Create("currency.invalid_format", $"'{value}' is not a three-letter currency code.", ErrorType.Validation)
                .WithArgument("value", value);
    }

    public string Code { get; }

    private Currency(string code) => Code = code;

    public static Result<Currency> Create(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return Errors.Required;

        var trimmed = input.Trim();
        return trimmed.Length == 3 && trimmed.All(char.IsAsciiLetter)
            ? new Currency(trimmed.ToUpperInvariant())
            : Errors.InvalidFormat(trimmed);
    }

    public override string ToString() => Code;
}
