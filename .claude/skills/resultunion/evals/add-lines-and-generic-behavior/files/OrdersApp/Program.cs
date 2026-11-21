using OrdersApp.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<IOrderRepository, InMemoryOrderRepository>();
builder.Services.AddProblemDetails();

var app = builder.Build();

// Endpoints go here.

app.Run();
