#if (Sqlite)
using Microsoft.EntityFrameworkCore;
using ResultUnionApi.Data;
#endif
using ResultUnionApi.Products;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();                   // unhandled exceptions become problem responses too

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

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();                                   // /openapi/v1.json

app.MapProducts();

app.Run();

/// <summary>The entry point, visible to the integration tests (WebApplicationFactory).</summary>
public partial class Program;
