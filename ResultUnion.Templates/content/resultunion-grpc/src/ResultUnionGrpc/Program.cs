using ResultUnionGrpc.Products;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGrpc();
builder.Services.AddGrpcReflection();                   // lets grpcurl, Postman and others discover the service

builder.Services.AddSingleton<IProductRepository, InMemoryProductRepository>();
builder.Services.AddScoped<ProductService>();

var app = builder.Build();

app.MapGrpcService<ProductCatalogService>();
app.MapGrpcReflectionService();
app.MapGet("/", () => "A gRPC service (HTTP/2). Call it with a gRPC client, or with grpcurl through reflection.");

app.Run();

/// <summary>The entry point, visible to the tests (WebApplicationFactory).</summary>
public partial class Program;
