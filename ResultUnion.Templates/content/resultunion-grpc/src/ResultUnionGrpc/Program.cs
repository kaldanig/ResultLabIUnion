#if (Sqlite)
using Microsoft.EntityFrameworkCore;
using ResultUnionGrpc.Data;
#endif
using ResultUnionGrpc;
using ResultUnionGrpc.Products;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGrpc();
builder.Services.AddGrpcReflectionWithErrorDetails();   // grpcurl and Postman discover the services and read failures

#if (Sqlite)
builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite(builder.Configuration.GetConnectionString("Products")));
builder.Services.AddScoped<IProductRepository, EfProductRepository>();
#else
builder.Services.AddSingleton<IProductRepository, InMemoryProductRepository>();
#endif
builder.Services.AddScoped<ProductService>();

var app = builder.Build();
#if (Sqlite)

// Creates the database on the first start. A real application uses migrations instead (dotnet ef migrations add).
using (var scope = app.Services.CreateScope())
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();
#endif

app.MapGrpcService<ProductCatalogService>();
app.MapGrpcReflectionService();
app.MapGet("/", () => "A gRPC service (HTTP/2). Call it with a gRPC client, or with grpcurl through reflection.");

app.Run();

/// <summary>The entry point, visible to the tests (WebApplicationFactory).</summary>
public partial class Program;
