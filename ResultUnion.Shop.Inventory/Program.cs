using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using ResultUnion.Diagnostics;
using ResultUnion.Shop.Inventory.Stock;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddSingleton<StockStore>();
builder.Services.AddSingleton<StockService>();

// Under ResultUnion.AppHost, OTLP goes to Aspire's dashboard, and the orders service's calls continue its traces here.
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddAspNetCoreInstrumentation())
    .WithMetrics(metrics => metrics.AddAspNetCoreInstrumentation().AddMeter(ResultTelemetry.MeterName))
    .WithLogging()
    .UseOtlpExporter();

var app = builder.Build();

app.UseExceptionHandler();
app.MapOpenApi();
app.MapStock();

await app.RunAsync();

/// <summary>The entry point, visible to the tests (WebApplicationFactory).</summary>
public partial class Program;
