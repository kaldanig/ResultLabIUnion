using ResultUnion;
using ResultUnion.AspNetCore;

namespace TargetNamespace.Gizmos;

public sealed record CreateWidgetRequest(string? Name);
#if (Crud)

/// <summary>A new name, with the version the client read: a stale version is 409 with <c>changed_concurrently</c>.</summary>
public sealed record RenameWidgetRequest(string? Name, int Version);

/// <summary>What a client receives. A <c>Result</c> is never serialized; its value is mapped to this.</summary>
public sealed record WidgetResponse(Guid Id, string Name, int Version)
{
    public static WidgetResponse From(Widget entity) => new(entity.Id, entity.Name.Value, entity.Version);
}
#else

/// <summary>What a client receives. A <c>Result</c> is never serialized; its value is mapped to this.</summary>
public sealed record WidgetResponse(Guid Id, string Name)
{
    public static WidgetResponse From(Widget entity) => new(entity.Id, entity.Name.Value);
}
#endif

#if (Sqlite)
/// <summary>
/// The feature's services and endpoints: <c>builder.Services.AddGizmos&lt;AppDbContext&gt;()</c> and
/// <c>app.MapGizmos()</c>, with <see cref="WidgetConfiguration"/> applied in the DbContext.
/// </summary>
public static class WidgetFeature
{
    public static IServiceCollection AddGizmos<TContext>(this IServiceCollection services)
        where TContext : Microsoft.EntityFrameworkCore.DbContext =>
        services
            .AddScoped<IWidgetRepository, EfWidgetRepository<TContext>>()
            .AddScoped<WidgetService>();
#else
/// <summary>The feature's services and endpoints: <c>builder.Services.AddGizmos()</c> and <c>app.MapGizmos()</c>.</summary>
public static class WidgetFeature
{
    public static IServiceCollection AddGizmos(this IServiceCollection services) =>
        services
            .AddSingleton<IWidgetRepository, InMemoryWidgetRepository>()
            .AddScoped<WidgetService>();
#endif

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
#if (Crud)

        group.MapPut("/{id:guid}", (Guid id, RenameWidgetRequest request, WidgetService service, CancellationToken ct) =>
                service.RenameAsync(new RenameWidget(id, request.Name, request.Version), ct)
                    .MapAsync(WidgetResponse.From)
                    .ToHttpResultAsync())
            .ProducesErrors(ErrorType.Validation, ErrorType.NotFound, ErrorType.Conflict, ErrorType.Concurrency);

        group.MapDelete("/{id:guid}", (Guid id, int version, WidgetService service, CancellationToken ct) =>
                service.RemoveAsync(id, version, ct)
                    .ToHttpResultAsync())  // 204 No Content
            .ProducesErrors(ErrorType.NotFound, ErrorType.Concurrency);
#endif

        return app;
    }
}
