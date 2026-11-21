using Scalar.AspNetCore;
using ResultUnion.Samples.Application;
using ResultUnion.WebApi.Endpoints;
using ResultUnion.WebApi.Localization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi(options => options.AddDocumentTransformer((document, _, _) =>
{
    document.Info.Title = "ResultUnion Web API";
    document.Info.Description =
        "A demo of the Result pattern on C# 15 unions. Every failure is an RFC 9457 problem response with a stable `code` " +
        "and `arguments`; validation problems list every invalid field. Send `Accept-Language: ka` for Georgian messages, " +
        "and `X-Role: admin` for the admin endpoint. ann@example.com is registered at startup.";
    return Task.CompletedTask;
}));
builder.Services.AddProblemDetails();                   // unhandled exceptions become problem responses too

builder.Services.AddSampleApplication();                // use cases and in-memory storage, shared with the gRPC service

var app = builder.Build();

app.UseExceptionHandler();

// A small browser UI for the API (wwwroot/index.html at /): plain HTML and JavaScript, no build step.
app.UseDefaultFiles();
app.UseStaticFiles();

// The API description and its UI. A demo, so they are always on; a real API would limit them to Development.
app.MapOpenApi();                                       // /openapi/v1.json
app.MapScalarApiReference(options => options.WithTitle("ResultUnion Web API"));   // /scalar

var api = app.MapGroup("").AddEndpointFilter<AcceptLanguageProblemFilter>();
api.MapUserEndpoints();
api.MapOrderEndpoints();
api.MapAdminEndpoints();

app.Services.SeedSampleData();                          // ann@example.com

app.Run();

/// <summary>The entry point, visible to integration tests (WebApplicationFactory).</summary>
public partial class Program;
