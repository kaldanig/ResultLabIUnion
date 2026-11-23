namespace ResultUnion.Consul.Rates.Rates;

public sealed record Rate(Currency From, Currency To, decimal Value);

/// <summary>Fixed rates against the US dollar; a real service would load them from a provider.</summary>
public sealed class RateTable
{
    public static class Errors
    {
        public static Error UnknownCurrency(Currency currency) =>
            Error.Create("rate.unknown_currency", $"There is no rate for {currency}.", ErrorType.NotFound)
                .WithArgument("currency", currency.Code);
    }

    private static readonly Dictionary<string, decimal> PerDollar = new(StringComparer.Ordinal)
    {
        ["USD"] = 1m,
        ["EUR"] = 0.92m,
        ["GBP"] = 0.79m,
        ["GEL"] = 2.71m,
        ["JPY"] = 151.4m,
    };

    public Result<Rate> Find(Currency from, Currency to) =>
        Result.CombineAll(PerDollarOf(from), PerDollarOf(to))
            .Map((fromRate, toRate) => new Rate(from, to, decimal.Round(toRate / fromRate, 6)));

    private static Result<decimal> PerDollarOf(Currency currency) =>
        PerDollar.TryGetValue(currency.Code, out var rate) ? rate : Errors.UnknownCurrency(currency);
}
