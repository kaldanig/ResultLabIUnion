namespace ResultUnion.Consul.Converter.Exchange;

/// <summary>A rate as one rates instance gave it.</summary>
public sealed record Rate(decimal Value, string ServedBy);

/// <summary>An amount converted at a rate, less the fee.</summary>
public sealed record Conversion(Amount Amount, Currency From, Currency To, Rate Rate, decimal FeePercent, decimal Fee, decimal Converted)
{
    public static class Errors
    {
        public static Error UnknownCurrency(string currency) =>
            Error.Create("conversion.unknown_currency", $"There is no rate for {currency}.", ErrorType.NotFound)
                .WithArgument("currency", currency);

        public static readonly Error Paused =
            Error.Create("conversion.paused", "Conversions are paused; try again later.", ErrorType.Unavailable);
    }

    public static Conversion Of(Amount amount, Currency from, Currency to, Rate rate, decimal feePercent)
    {
        var gross = amount.Value * rate.Value;
        var fee = decimal.Round(gross * feePercent / 100m, 2, MidpointRounding.AwayFromZero);
        return new Conversion(amount, from, to, rate, feePercent, fee, decimal.Round(gross, 2, MidpointRounding.AwayFromZero) - fee);
    }
}
