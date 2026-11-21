using ResultUnion.GrpcService.Services;
using ResultUnion.Samples.Application;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGrpc();
builder.Services.AddGrpcReflection();                   // lets grpcurl, Postman and others discover the services
builder.Services.AddSampleApplication();                // the same use cases and storage as the Web API

var app = builder.Build();

app.MapGrpcService<UsersGrpcService>();
app.MapGrpcService<OrdersGrpcService>();
app.MapGrpcReflectionService();
app.MapGet("/", () => "ResultUnion gRPC demo: gRPC only (HTTP/2). Run ResultUnion.GrpcClient, or use grpcurl with reflection.");

app.Services.SeedSampleData();                          // ann@example.com

app.Run();
