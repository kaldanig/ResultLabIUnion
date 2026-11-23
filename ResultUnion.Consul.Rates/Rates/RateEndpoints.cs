using ResultUnion.AspNetCore;
using ResultUnion.Consul.Rates.Registration;

namespace ResultUnion.Consul.Rates.Rates;

/// <param name="ServedBy">The instance that answered, so the converter (and a person trying the demo) can see which one it reached.</param>
public sealed record RateResponse(string From, string To, decimal Rate, string ServedBy);

public static class RateEndpoints
{
    public static IEndpointRouteBuilder MapRates(this IEndpointRouteBuilder app)
    {
        app.MapGet("/rates/{from}/{to}", (string from, string to, RateService service, RatesInstance instance) =>
                service.Find(from, to)
                    .Map(rate => new RateResponse(rate.From.Code, rate.To.Code, rate.Value, instance.Id))
                    .ToHttpResult())
            .WithTags("Rates")
            .ProducesErrors(ErrorType.Validation, ErrorType.NotFound, ErrorType.Unavailable);

        // The demo's switch; not something a real rates service would expose.
        var outage = app.MapGroup("/outage").WithTags("Outage");
        outage.MapPost("/", (Outage switchOf) => { switchOf.Start(); return TypedResults.NoContent(); });
        outage.MapDelete("/", (Outage switchOf) => { switchOf.End(); return TypedResults.NoContent(); });

        return app;
    }
}
