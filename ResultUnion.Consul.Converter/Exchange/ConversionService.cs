using ResultUnion.Consul.Converter.Discovery;
using ResultUnion.Consul.Converter.Rates;

namespace ResultUnion.Consul.Converter.Exchange;

/// <summary>The raw input, from the query string.</summary>
public sealed record ConvertCommand(string? Amount, string? From, string? To);

/// <summary>
/// A conversion as one pipeline: every input at once, then the settings from Consul's key/value store, then the
/// healthy rates instances from its catalog, then the first instance that gives an answer.
/// </summary>
public sealed partial class ConversionService(
    SettingsStore settings,
    ServiceDiscovery discovery,
    RatesClient rates,
    ILogger<ConversionService> logger)
{
    /// <summary>The name the rates instances register under.</summary>
    public const string RatesService = "rates";

    public Task<Result<Conversion>> ConvertAsync(ConvertCommand command, CancellationToken ct) =>
        Result.CombineAll(
                Amount.Create(command.Amount).MapError(e => e.WithField("amount")),
                Currency.Create(command.From).MapError(e => e.WithField("from")),
                Currency.Create(command.To).MapError(e => e.WithField("to")))
            .BindAsync(valid => ConvertAsync(valid.Item1, valid.Item2, valid.Item3, ct))
            .TapErrorAsync(LogIfUnexpected)
            .RecordAsync("converter.convert");

    private Task<Result<Conversion>> ConvertAsync(Amount amount, Currency from, Currency to, CancellationToken ct) =>
        settings.ReadAsync(ct)
            .EnsureAsync(current => !current.Paused, Conversion.Errors.Paused)
            .BindAsync(current => discovery.FindAsync(RatesService, ct)
                .BindAsync(instances => FirstAnswerAsync(instances, from, to, ct))
                .MapAsync(rate => Conversion.Of(amount, from, to, rate, current.FeePercent)))
            .MapErrorAsync(error => NameTheField(error, from));

    // Consul lists an instance until its check fails, which takes up to a heartbeat; an instance that fails in between
    // answers 503 or not at all. Both are transient, so the next instance gets the same request. A non-transient
    // answer (no rate for the currency) would be the same from every instance and is returned at once.
    private async Task<Result<Rate>> FirstAnswerAsync(IReadOnlyList<Uri> instances, Currency from, Currency to, CancellationToken ct)
    {
        Result<Rate> answer = ServiceDiscovery.Errors.NoHealthyInstance(RatesService);
        foreach (var instance in instances)
        {
            answer = await rates.GetAsync(instance, from, to, ct).ConfigureAwait(false);
            if (!answer.TryGetError(out var error) || !error.IsTransient)
                return answer;

            LogTryingNext(instance, error.Code);
        }

        return answer;
    }

    // The rates service names the currency it has no rate for; the converter knows which query parameter that was.
    private static Error NameTheField(Error error, Currency from) =>
        error.Code == "conversion.unknown_currency"
            ? error.WithField(error.Arguments.TryGetValue("currency", out var value) && value is string code && code == from.Code ? "from" : "to")
            : error;

    private void LogIfUnexpected(Error error)
    {
        if (error.Type == ErrorType.Unexpected)
            LogUnexpected(error.Code, error.Message);
    }

    [LoggerMessage(LogLevel.Warning, "The rates instance at {Instance} failed with {Code}; trying the next one.")]
    private partial void LogTryingNext(Uri instance, string code);

    [LoggerMessage(LogLevel.Error, "A conversion failed with {Code}: {Message}")]
    private partial void LogUnexpected(string code, string message);
}
