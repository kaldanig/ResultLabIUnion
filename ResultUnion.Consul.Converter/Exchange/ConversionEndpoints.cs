using ResultUnion.AspNetCore;
using ResultUnion.Consul.Converter.Discovery;

namespace ResultUnion.Consul.Converter.Exchange;

/// <param name="ServedBy">The rates instance that gave the rate.</param>
public sealed record ConversionResponse(
    decimal Amount, string From, string To, decimal Rate, decimal FeePercent, decimal Fee, decimal Converted, string ServedBy)
{
    public static ConversionResponse Of(Conversion conversion) => new(
        conversion.Amount.Value, conversion.From.Code, conversion.To.Code, conversion.Rate.Value,
        conversion.FeePercent, conversion.Fee, conversion.Converted, conversion.Rate.ServedBy);
}

public static class ConversionEndpoints
{
    public static IEndpointRouteBuilder MapConversions(this IEndpointRouteBuilder app)
    {
        app.MapGet("/convert", (string? amount, string? from, string? to, ConversionService service, CancellationToken ct) =>
                service.ConvertAsync(new ConvertCommand(amount, from, to), ct)
                    .MapAsync(ConversionResponse.Of)
                    .ToHttpResultAsync())
            .WithTags("Conversion")
            .ProducesErrors(ErrorType.Validation, ErrorType.NotFound, ErrorType.Unavailable);

        // What Consul lists right now: start or stop a rates instance, or switch its outage on, and watch it change.
        app.MapGet("/instances", (ServiceDiscovery discovery, CancellationToken ct) =>
                discovery.FindAsync(ConversionService.RatesService, ct)
                    .MapAsync(instances => instances.Select(instance => instance.ToString()).Order(StringComparer.Ordinal).ToList())
                    .ToHttpResultAsync())
            .WithTags("Discovery")
            .ProducesErrors(ErrorType.Unavailable);

        return app;
    }
}
