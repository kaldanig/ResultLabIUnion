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

// The shop sample: the orders service reserves stock in the inventory service over HTTP. WithReference gives it the
// inventory's address (services__inventory__http__0), and one trace shows an order and its reservation.
var inventory = builder.AddDotnetProject("inventory", "../ResultUnion.Shop.Inventory/ResultUnion.Shop.Inventory.csproj");
var orders = builder.AddDotnetProject("orders", "../ResultUnion.Shop.Orders/ResultUnion.Shop.Orders.csproj")
    .WithReference(inventory)
    .WaitFor(inventory);

// The shop's web front end calls both services with ResultUnion.Http; stop the inventory in the dashboard to see an
// order refused with inventory.unavailable.
builder.AddDotnetProject("shop", "../ResultUnion.Shop.Web/ResultUnion.Shop.Web.csproj")
    .WithReference(orders)
    .WithReference(inventory)
    .WaitFor(orders)
    .WithExternalHttpEndpoints();

builder.Build().Run();
