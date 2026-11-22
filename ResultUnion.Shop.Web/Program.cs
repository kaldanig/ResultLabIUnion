using OpenTelemetry;
using OpenTelemetry.Trace;
using ResultUnion.Shop.Web;
using ResultUnion.Shop.Web.Components;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents().AddInteractiveServerComponents();

// Under ResultUnion.AppHost the addresses come from Aspire (WithReference); run alone, from appsettings.json.
foreach (var service in new[] { ShopClient.Orders, ShopClient.Inventory })
{
    var address = builder.Configuration[$"services:{service}:http:0"] ?? builder.Configuration[$"Shop:{service}"]
        ?? throw new InvalidOperationException($"Set Shop:{service} to the {service} service's address.");
    builder.Services.AddHttpClient(service, http =>
    {
        http.BaseAddress = new Uri(address);
        http.Timeout = TimeSpan.FromSeconds(5);
    });
}

builder.Services.AddScoped<ShopLanguage>();       // one per browser tab (Blazor circuit)
builder.Services.AddScoped<ShopClient>();

// A click in the page and the calls it makes to the orders and inventory services are one trace.
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation())
    .WithLogging()
    .UseOtlpExporter();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/not-found", createScopeForErrors: true);

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

await app.RunAsync();

/// <summary>The entry point, visible to the tests (WebApplicationFactory).</summary>
public partial class Program;
