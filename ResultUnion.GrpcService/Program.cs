using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using ResultUnion.Diagnostics;
using ResultUnion.GrpcService.Services;
using ResultUnion.Samples.Application;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGrpc();
builder.Services.AddGrpcReflection();                   // lets grpcurl, Postman and others discover the services
builder.Services.AddSampleApplication();                // the same use cases and storage as the Web API

// OpenTelemetry over OTLP to OTEL_EXPORTER_OTLP_ENDPOINT, which ResultUnion.AppHost sets to its dashboard. A failed call
// tags its span through GetValueOrThrowRpcAsync; the meter adds the failure counter from RecordAsync in the use cases.
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddAspNetCoreInstrumentation())
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddMeter(ResultTelemetry.MeterName))                                   // resultunion.errors
    .WithLogging()
    .UseOtlpExporter();

var app = builder.Build();

app.MapGrpcService<UsersGrpcService>();
app.MapGrpcService<OrdersGrpcService>();
app.MapGrpcReflectionService();
app.MapGet("/", () => "ResultUnion gRPC demo: gRPC only (HTTP/2). Run ResultUnion.GrpcClient, or use grpcurl with reflection.");

app.Services.SeedSampleData();                          // ann@example.com

app.Run();
