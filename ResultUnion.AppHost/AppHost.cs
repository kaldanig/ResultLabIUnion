// The demos under Aspire. Aspire starts each one with OTEL_EXPORTER_OTLP_ENDPOINT pointing at its dashboard, so the
// OpenTelemetry setup the demos already have sends their traces, metrics and logs there; nothing here is specific to
// ResultUnion. In the dashboard, the metric resultunion.errors counts failures by code, and a failed request's span
// carries resultunion.error.code and resultunion.error.category.
var builder = DistributedApplication.CreateBuilder(args);

builder.AddDotnetProject("webapi", "../ResultUnion.WebApi/ResultUnion.WebApi.csproj")
    .WithExternalHttpEndpoints();

var grpc = builder.AddDotnetProject("grpcservice", "../ResultUnion.GrpcService/ResultUnion.GrpcService.csproj");

// Runs every call of the gRPC demo once and stops; restart it from the dashboard for another round. Its calls and the
// service's handling of them are one trace each, across the two resources.
builder.AddDotnetProject("grpcclient", "../ResultUnion.GrpcClient/ResultUnion.GrpcClient.csproj")
    .WithArgs(context => context.Args.Add(grpc.GetEndpoint("http")))
    .WaitFor(grpc);

builder.Build().Run();
