using ResultUnion.Consul.Converter.Exchange;
using ResultUnion.Http;

namespace ResultUnion.Consul.Converter.Rates;

/// <summary>
/// One rates instance, as the converter sees it. The address comes from Consul for each call, so the client has no
/// base address. ResultUnion.Http reads each answer back as a result with the rates service's own error, and this
/// adapter turns every outcome into an error of the converter's catalog:
/// <list type="bullet">
///   <item><description>a currency without a rate is the client's problem (404, <c>conversion.unknown_currency</c>);</description></item>
///   <item><description>an instance that cannot be reached, times out or answers 503 (an outage) is <c>rates.unavailable</c>:
///   transient, so the converter tries the next instance;</description></item>
///   <item><description>anything else means the two services disagree: a bug, reported as unexpected (500).</description></item>
/// </list>
/// </summary>
public sealed class RatesClient(HttpClient http)
{
    public static class Errors
    {
        public static Error Unavailable(Uri instance) =>
            Error.Create("rates.unavailable", $"The rates instance at {instance} cannot answer now.", ErrorType.Unavailable)
                .WithArgument("instance", instance.ToString());

        public static Error UnexpectedAnswer(string code) =>
            Error.Create("rates.unexpected_answer", $"The rates service answered '{code}', which a conversion should never cause.", ErrorType.Unexpected)
                .WithArgument("code", code);
    }

    private sealed record RateAnswer(string From, string To, decimal Rate, string ServedBy);

    public async Task<Result<Rate>> GetAsync(Uri instance, Currency from, Currency to, CancellationToken ct)
    {
        Result<RateAnswer> answer;
        try
        {
            answer = await http.GetAsync(new Uri(instance, $"/rates/{from}/{to}"), ct).ReadResultAsync<RateAnswer>(ct).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            return Errors.Unavailable(instance);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return Errors.Unavailable(instance);
        }

        return answer
            .Map(rate => new Rate(rate.Rate, rate.ServedBy))
            .MapError(error => Translate(error, instance));
    }

    private static Error Translate(Error error, Uri instance) => error.Code switch
    {
        "rate.unknown_currency" => Conversion.Errors.UnknownCurrency(
            error.Arguments.TryGetValue("currency", out var value) && value is string currency ? currency : "?"),
        _ when error.Type == ErrorType.Unavailable => Errors.Unavailable(instance),
        _ => Errors.UnexpectedAnswer(error.Code),
    };
}
