using System.Globalization;
using System.Text;
using Consul;

namespace ResultUnion.Consul.Converter.Discovery;

/// <param name="FeePercent">The share of each conversion kept as a fee, from 0 up to (not including) 100.</param>
/// <param name="Paused">While true, every conversion is refused with <c>conversion.paused</c>.</param>
public sealed record ConverterSettings(decimal FeePercent, bool Paused)
{
    public static readonly ConverterSettings Default = new(0m, false);
}

/// <summary>
/// The converter's settings in Consul's key/value store, read on every request, so a change in the Consul UI applies
/// to the next conversion. A key that is not there takes its default; a key that holds something else is not the
/// client's problem but an operator's, so it is an <c>Unexpected</c> error (500, logged) rather than a guess.
/// </summary>
public sealed class SettingsStore(IConsulClient consul)
{
    public const string FeeKey = "converter/fee-percent";

    public const string PausedKey = "converter/paused";

    public static class Errors
    {
        public static Error Invalid(string key, string value) =>
            Error.Create("settings.invalid", $"The Consul key {key} holds '{value}', which is not a valid value.", ErrorType.Unexpected)
                .WithArgument("key", key)
                .WithArgument("value", value);
    }

    private delegate bool Parser<T>(string text, out T value)
        where T : notnull;

    public Task<Result<ConverterSettings>> ReadAsync(CancellationToken ct) =>
        ConsulCall.RunAsync(() => consul.KV.List("converter/", ct), ct)
            .BindAsync(answer => Parse(answer.Response ?? []));

    private static Result<ConverterSettings> Parse(KVPair[] pairs)
    {
        var values = pairs.ToDictionary(
            pair => pair.Key,
            pair => pair.Value is null ? "" : Encoding.UTF8.GetString(pair.Value),
            StringComparer.Ordinal);

        return Result.CombineAll(
                Read<decimal>(values, FeeKey, ConverterSettings.Default.FeePercent, TryParseFee),
                Read<bool>(values, PausedKey, ConverterSettings.Default.Paused, (string text, out bool value) => bool.TryParse(text, out value)))
            .Map((fee, paused) => new ConverterSettings(fee, paused));
    }

    private static Result<T> Read<T>(Dictionary<string, string> values, string key, T fallback, Parser<T> parse)
        where T : notnull
    {
        if (!values.TryGetValue(key, out var text))
            return fallback;

        return parse(text.Trim(), out var value) ? value : Errors.Invalid(key, text);
    }

    private static bool TryParseFee(string text, out decimal fee) =>
        decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out fee) && fee is >= 0m and < 100m;
}
