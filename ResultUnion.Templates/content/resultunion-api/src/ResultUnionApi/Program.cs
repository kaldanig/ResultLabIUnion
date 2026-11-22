using ResultUnionApi.Products;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();                   // unhandled exceptions become problem responses too

builder.Services.AddSingleton<IProductRepository, InMemoryProductRepository>();
builder.Services.AddScoped<ProductService>();

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();                                   // /openapi/v1.json

app.MapProducts();

app.Run();

/// <summary>The entry point, visible to the integration tests (WebApplicationFactory).</summary>
public partial class Program;
