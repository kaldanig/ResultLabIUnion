namespace TargetNamespace.Gizmos;

#if (Sqlite)
/// <summary>
/// The feature's services and gRPC service: <c>builder.Services.AddGizmos&lt;AppDbContext&gt;()</c> and
/// <c>app.MapGizmos()</c>, with <see cref="WidgetConfiguration"/> applied in the DbContext. The contract is
/// <c>Protos/PLURAL_CODE.proto</c>.
/// </summary>
public static class WidgetFeature
{
    public static IServiceCollection AddGizmos<TContext>(this IServiceCollection services)
        where TContext : Microsoft.EntityFrameworkCore.DbContext =>
        services
            .AddScoped<IWidgetRepository, EfWidgetRepository<TContext>>()
            .AddScoped<WidgetService>();
#else
/// <summary>
/// The feature's services and gRPC service: <c>builder.Services.AddGizmos()</c> and <c>app.MapGizmos()</c>. The
/// contract is <c>Protos/PLURAL_CODE.proto</c>.
/// </summary>
public static class WidgetFeature
{
    public static IServiceCollection AddGizmos(this IServiceCollection services) =>
        services
            .AddSingleton<IWidgetRepository, InMemoryWidgetRepository>()
            .AddScoped<WidgetService>();
#endif

    public static IEndpointRouteBuilder MapGizmos(this IEndpointRouteBuilder app)
    {
        app.MapGrpcService<WidgetCatalogService>();
        return app;
    }
}
