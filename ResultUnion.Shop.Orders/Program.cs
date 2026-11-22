using Microsoft.EntityFrameworkCore;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using ResultUnion.Diagnostics;
using ResultUnion.Shop.Orders.Customers;
using ResultUnion.Shop.Orders.Data;
using ResultUnion.Shop.Orders.Localization;
using ResultUnion.Shop.Orders.Orders;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddDbContext<ShopDbContext>(options => options.UseSqlite(builder.Configuration.GetConnectionString("Shop")));
builder.Services.AddScoped<CustomerService>();
builder.Services.AddScoped<OrderService>();
builder.Services.AddSingleton(TimeProvider.System);

// Under ResultUnion.AppHost the inventory's address comes from Aspire (WithReference); run alone, from appsettings.json.
var inventory = builder.Configuration["services:inventory:http:0"] ?? builder.Configuration["Inventory:BaseAddress"]
    ?? throw new InvalidOperationException("Set Inventory:BaseAddress to the inventory service's address.");
builder.Services.AddHttpClient<InventoryClient>(http =>
{
    http.BaseAddress = new Uri(inventory);
    http.Timeout = TimeSpan.FromSeconds(5);         // a slow inventory is an unavailable one (inventory.unavailable, 503)
});

// Problem messages in the request's language: Accept-Language: ka gives Georgian, anything else the English ones.
builder.Services.AddLocalization();
builder.Services.AddSingleton<ProblemTranslator>();
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
    context.HttpContext.RequestServices.GetRequiredService<ProblemTranslator>().Translate(context.ProblemDetails));

// The calls to the inventory continue this service's traces there, so one trace shows an order and its reservation.
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation())
    .WithMetrics(metrics => metrics.AddAspNetCoreInstrumentation().AddMeter(ResultTelemetry.MeterName))
    .WithLogging()
    .UseOtlpExporter();

var app = builder.Build();

// Creates the database on the first start. A real application uses migrations instead (dotnet ef migrations add).
await using (var scope = app.Services.CreateAsyncScope())
    await scope.ServiceProvider.GetRequiredService<ShopDbContext>().Database.EnsureCreatedAsync();

app.UseExceptionHandler();
app.UseRequestLocalization(options => options
    .SetDefaultCulture("en")
    .AddSupportedCultures("en", "ka")
    .AddSupportedUICultures("en", "ka"));
app.MapOpenApi();
app.MapCustomers();
app.MapOrders();

await app.RunAsync();

/// <summary>The entry point, visible to the tests (WebApplicationFactory).</summary>
public partial class Program;
