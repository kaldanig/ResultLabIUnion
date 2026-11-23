using System.Globalization;

namespace ResultUnion.Consul.Converter.Exchange;

/// <summary>A positive amount of money, as the query string gives it (<c>?amount=12.50</c>, invariant culture).</summary>
public sealed record Amount
{
    public static class Errors
    {
        public static readonly Error Required =
            Error.Create("amount.required", "An amount is required.", ErrorType.Validation);

        public static Error NotANumber(string value) =>
            Error.Create("amount.not_a_number", $"'{value}' is not a number.", ErrorType.Validation)
                .WithArgument("value", value);

        public static Error NotPositive(decimal value) =>
            Error.Create("amount.not_positive", $"The amount must be positive, was {value}.", ErrorType.Validation)
                .WithArgument("value", value);
    }

    public decimal Value { get; }

    private Amount(decimal value) => Value = value;

    public static Result<Amount> Create(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return Errors.Required;

        var trimmed = input.Trim();
        if (!decimal.TryParse(trimmed, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
            return Errors.NotANumber(trimmed);

        return value > 0 ? new Amount(value) : Errors.NotPositive(value);
    }
}
