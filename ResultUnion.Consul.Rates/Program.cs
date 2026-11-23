using Consul;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using ResultUnion.Consul.Rates.Rates;
using ResultUnion.Consul.Rates.Registration;
using ResultUnion.Diagnostics;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddSingleton<RateTable>();
builder.Services.AddSingleton<Outage>();
builder.Services.AddSingleton<RateService>();

// Under ResultUnion.AppHost, Consul:Address is the Consul container's endpoint; run alone, the agent on localhost:8500.
builder.Services.Configure<ConsulOptions>(builder.Configuration.GetSection("Consul"));
builder.Services.AddSingleton<IConsulClient>(services => new ConsulClient(
    consul => consul.Address = services.GetRequiredService<IOptions<ConsulOptions>>().Value.Address,
    http => http.Timeout = TimeSpan.FromSeconds(5)));     // a hung agent is an unreachable one (consul.unreachable)
builder.Services.AddSingleton<ConsulAgent>();
builder.Services.AddSingleton<RatesInstance>();
builder.Services.AddHostedService<ConsulRegistration>();

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddAspNetCoreInstrumentation())
    .WithMetrics(metrics => metrics.AddAspNetCoreInstrumentation().AddMeter(ResultTelemetry.MeterName))
    .WithLogging()
    .UseOtlpExporter();

var app = builder.Build();

app.UseExceptionHandler();
app.MapOpenApi();
app.MapRates();

await app.RunAsync();

/// <summary>The entry point, visible to the tests (WebApplicationFactory).</summary>
public partial class Program;
