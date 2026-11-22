using Grpc.Reflection;
using ResultUnionGrpc.Products;
using ResultUnionGrpc.Products.V1;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGrpc();

// Reflection lets grpcurl, Postman and others discover the service. By default it describes only the service's own
// messages; with error_details.proto as well, they show a failure's ErrorInfo and BadRequest as JSON instead of bytes.
// A new gRPC service goes into this list too.
builder.Services.AddGrpcReflection();
string[] reflectedServices = [ProductCatalog.Descriptor.FullName];
var reflectedFiles = SymbolRegistry.FromFiles([ProductCatalog.Descriptor.File, Google.Rpc.ErrorDetailsReflection.Descriptor]);
builder.Services.AddSingleton(new ReflectionServiceImpl(reflectedServices, reflectedFiles));
builder.Services.AddSingleton(new ReflectionV1ServiceImpl(reflectedServices, reflectedFiles));

builder.Services.AddSingleton<IProductRepository, InMemoryProductRepository>();
builder.Services.AddScoped<ProductService>();

var app = builder.Build();

app.MapGrpcService<ProductCatalogService>();
app.MapGrpcReflectionService();
app.MapGet("/", () => "A gRPC service (HTTP/2). Call it with a gRPC client, or with grpcurl through reflection.");

app.Run();

/// <summary>The entry point, visible to the tests (WebApplicationFactory).</summary>
public partial class Program;
