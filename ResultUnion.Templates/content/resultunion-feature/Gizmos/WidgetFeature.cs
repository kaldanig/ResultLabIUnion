using ResultUnion;
using ResultUnion.AspNetCore;

namespace TargetNamespace.Gizmos;

public sealed record CreateWidgetRequest(string? Name);

/// <summary>What a client receives. A <c>Result</c> is never serialized; its value is mapped to this.</summary>
public sealed record WidgetResponse(Guid Id, string Name)
{
    public static WidgetResponse From(Widget entity) => new(entity.Id, entity.Name.Value);
}

/// <summary>The feature's services and endpoints: <c>builder.Services.AddGizmos()</c> and <c>app.MapGizmos()</c>.</summary>
public static class WidgetFeature
{
    public static IServiceCollection AddGizmos(this IServiceCollection services) =>
        services
            .AddSingleton<IWidgetRepository, InMemoryWidgetRepository>()
            .AddScoped<WidgetService>();

    /// <summary>
    /// A success becomes the typed response, a failure an RFC 9457 problem with the error's <c>code</c>, its
    /// arguments and, for validation, the invalid field. ProducesErrors documents the possible problems in OpenAPI.
    /// </summary>
    public static IEndpointRouteBuilder MapGizmos(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/PLURAL_ROUTE").WithTags("Gizmos");

        group.MapPost("/", (CreateWidgetRequest request, WidgetService service, CancellationToken ct) =>
                service.CreateAsync(new CreateWidget(request.Name), ct)
                    .MapAsync(WidgetResponse.From)
                    .ToHttpResultAsync(created => TypedResults.Created($"/PLURAL_ROUTE/{created.Id}", created)))
            .ProducesErrors(ErrorType.Validation, ErrorType.Conflict);

        group.MapGet("/{id:guid}", (Guid id, WidgetService service, CancellationToken ct) =>
                service.FindAsync(id, ct)
                    .MapAsync(WidgetResponse.From)
                    .ToHttpResultAsync())
            .ProducesErrors(ErrorType.NotFound);

        return app;
    }
}
