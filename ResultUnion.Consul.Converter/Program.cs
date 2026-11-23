using Consul;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using ResultUnion.Consul.Converter.Discovery;
using ResultUnion.Consul.Converter.Exchange;
using ResultUnion.Consul.Converter.Rates;
using ResultUnion.Diagnostics;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();

// Under ResultUnion.AppHost, Consul:Address is the Consul container's endpoint; run alone, the agent on localhost:8500.
var consulAddress = new Uri(builder.Configuration["Consul:Address"] ?? "http://localhost:8500");
builder.Services.AddSingleton<IConsulClient>(_ => new ConsulClient(
    consul => consul.Address = consulAddress,
    http => http.Timeout = TimeSpan.FromSeconds(5)));     // a hung agent is an unreachable one (consul.unreachable, 503)
builder.Services.AddSingleton<ServiceDiscovery>();
builder.Services.AddSingleton<SettingsStore>();
builder.Services.AddScoped<ConversionService>();

// No base address: each call goes to an instance Consul listed for it.
builder.Services.AddHttpClient<RatesClient>(http => http.Timeout = TimeSpan.FromSeconds(3));   // a slow instance is an unavailable one

// The calls to the rates continue this service's traces there, so one trace shows a conversion, the instances it
// tried, and the one that answered.
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation())
    .WithMetrics(metrics => metrics.AddAspNetCoreInstrumentation().AddMeter(ResultTelemetry.MeterName))
    .WithLogging()
    .UseOtlpExporter();

var app = builder.Build();

app.UseExceptionHandler();
app.MapOpenApi();
app.MapConversions();

await app.RunAsync();

/// <summary>The entry point, visible to the tests (WebApplicationFactory).</summary>
public partial class Program;
