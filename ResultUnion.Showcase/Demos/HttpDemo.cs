using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using ResultUnion.AspNetCore;

namespace ResultUnion.Showcase.Demos;

/// <summary>ResultUnion.AspNetCore: results as Minimal API responses, shown as the HTTP response a client receives.</summary>
internal static class HttpDemo
{
    private static readonly JsonSerializerOptions Pretty = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static async Task RunAsync()
    {
        var service = new RegistrationService(new UserRepository());

        // Success with a custom response: 201 Created.
        await SendAsync("POST /users  (ok)", service.Register("ann@example.com", "34")
            .Map(user => new { user.Id, Email = user.Email.Value, user.Age })
            .ToHttpResult(user => TypedResults.Created($"/users/{user.Id}", user)));

        // A domain error: status from ErrorType, the code as an extension.
        await SendAsync("POST /users  (duplicate)", service.Register("ann@example.com", "40").ToHttpResult());

        // Several invalid fields: a validation problem with errors per field and details with codes.
        var form = Result.CombineAll(
            Email.Create("dan@").MapError(error => error.WithField("email")),
            Password.Create("123").MapError(error => error.WithField("password")));
        await SendAsync("POST /users  (invalid form)", form.ToHttpResult());

        // A collection: nested errors are flattened, each with its full field path.
        OrderLineInput[] lines = [new("apple", 2), new("", 0), new("pear", -1)];
        await SendAsync("POST /orders (invalid lines)", lines.CombineAll(OrderLine.Create, field: "lines").ToHttpResult());

        // Unexpected errors do not leak their message.
        await SendAsync("GET /reports (crash)", Result<int>.Failure(Error.Unexpected("NullReferenceException in ReportBuilder.Build()")).ToHttpResult());

        ShowOpenApiMetadata();
    }

    // What OpenAPI / Swagger sees: typed responses document the success, ProducesErrors documents the problems.
    private static void ShowOpenApiMetadata()
    {
        var app = WebApplication.CreateSlimBuilder().Build();
        var service = new RegistrationService(new UserRepository());

        app.MapPost("/users", (string email, string age) => service.Register(email, age)
                .Map(user => new UserResponse(user.Id, user.Email.Value))
                .ToHttpResult(user => TypedResults.Created($"/users/{user.Id}", user)))
            .ProducesErrors(ErrorType.Validation, ErrorType.Conflict, ErrorType.BusinessRule);

        app.MapDelete("/users/{email}", (string email) => service.Deactivate(email).ToHttpResult())
            .ProducesErrors(ErrorType.NotFound, ErrorType.Conflict);

        Console.WriteLine();
        Console.WriteLine("  OpenAPI metadata (what Swagger documents):");
        foreach (var endpoint in ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints))
        {
            var responses = endpoint.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>()
                .Select(metadata => $"{metadata.StatusCode} {metadata.Type?.Name}".TrimEnd());

            Demo.Show($"    {endpoint.DisplayName?.Replace("HTTP: ", "")}", string.Join(", ", responses));
        }
    }

    private sealed record UserResponse(Guid Id, string Email);

    private static async Task SendAsync(string request, IResult response)
    {
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider(),
        };
        using var body = new MemoryStream();
        context.Response.Body = body;

        await response.ExecuteAsync(context);

        Console.WriteLine();
        Console.ForegroundColor = context.Response.StatusCode < 400 ? ConsoleColor.Green : ConsoleColor.Yellow;
        Console.WriteLine($"  {request}  ->  {context.Response.StatusCode} {context.Response.ContentType}");
        Console.ResetColor();

        if (body.Length > 0)
        {
            body.Position = 0;
            using var json = JsonDocument.Parse(body);
            var text = JsonSerializer.Serialize(json.RootElement, Pretty);
            Console.WriteLine(string.Join(Environment.NewLine, text.Split('\n').Select(line => "    " + line.TrimEnd('\r'))));
        }
    }
}
