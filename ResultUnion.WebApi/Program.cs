using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Scalar.AspNetCore;
using ResultUnion.Diagnostics;
using ResultUnion.Samples.Application;
using ResultUnion.WebApi.Endpoints;
using ResultUnion.WebApi.Localization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi(options => options.AddDocumentTransformer((document, _, _) =>
{
    document.Info.Title = "ResultUnion Web API";
    document.Info.Description =
        "A demo of the Result pattern on C# 15 unions. Every failure is an RFC 9457 problem response with a stable `code` " +
        "and `arguments`; validation problems list every invalid field. Send `Accept-Language: ka` for Georgian messages, " +
        "and `X-Role: admin` for the admin endpoint. ann@example.com is registered at startup.";
    return Task.CompletedTask;
}));
builder.Services.AddProblemDetails();                   // unhandled exceptions become problem responses too

builder.Services.AddSampleApplication();                // use cases and in-memory storage, shared with the gRPC service

// OpenTelemetry over OTLP to OTEL_EXPORTER_OTLP_ENDPOINT (http://localhost:4317 in launchSettings.json), for example the
// standalone Aspire Dashboard (see the README). Adding ResultUnion's meter is the only ResultUnion-specific line: the
// failure counter comes from RecordAsync in the use cases, and problem responses tag the request spans by themselves.
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("ResultUnion.WebApi"))
    .WithTracing(tracing => tracing.AddAspNetCoreInstrumentation(options =>
        options.Filter = context => IsApiRequest(context.Request.Path)))
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddMeter(ResultTelemetry.MeterName))                                   // resultunion.errors
    .WithLogging()
    .UseOtlpExporter();

var app = builder.Build();

app.UseExceptionHandler();

// A small browser UI for the API (wwwroot/index.html at /): plain HTML and JavaScript, no build step.
app.UseDefaultFiles();
app.UseStaticFiles();

// The API description and its UI. A demo, so they are always on; a real API would limit them to Development.
app.MapOpenApi();                                       // /openapi/v1.json
app.MapScalarApiReference(options => options.WithTitle("ResultUnion Web API"));   // /scalar

var api = app.MapGroup("").AddEndpointFilter<AcceptLanguageProblemFilter>();
api.MapUserEndpoints();
api.MapOrderEndpoints();
api.MapAdminEndpoints();

app.Services.SeedSampleData();                          // ann@example.com

app.Run();

// Traces show the API calls only, not the browser UI's files, Scalar or the OpenAPI document.
static bool IsApiRequest(PathString path) =>
    !Path.HasExtension(path.Value)
    && !path.StartsWithSegments("/scalar")
    && !path.StartsWithSegments("/openapi");

/// <summary>The entry point, visible to integration tests (WebApplicationFactory).</summary>
public partial class Program;
