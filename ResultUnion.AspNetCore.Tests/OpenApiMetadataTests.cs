using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace ResultUnion.AspNetCore.Tests;

/// <summary>What OpenAPI sees: the response metadata ASP.NET Core collects for Minimal API endpoints.</summary>
public class OpenApiMetadataTests
{
    private sealed record UserDto(int Id);

    private static readonly Result<UserDto> Found = Result<UserDto>.Success(new UserDto(1));

    private static IReadOnlyList<(int Status, Type? Type)> Produces(Action<WebApplication> map)
    {
        var app = WebApplication.CreateSlimBuilder().Build();
        map(app);

        var endpoint = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints).Single();

        return endpoint.Metadata
            .GetOrderedMetadata<IProducesResponseTypeMetadata>()
            .Select(metadata => (metadata.StatusCode, metadata.Type))
            .ToArray();
    }

    [Fact]
    public void TypedResponse_DocumentsTheSuccess()
    {
        var produces = Produces(app => app.MapGet("/users/{id}", (int id) => Found.ToHttpResult()));

        Assert.Contains((200, typeof(UserDto)), produces);
    }

    [Fact]
    public void TypedCustomResponse_DocumentsTheGivenSuccess()
    {
        var produces = Produces(app => app.MapPost("/users", () => Found.ToHttpResult(user => TypedResults.Created($"/users/{user.Id}", user))));

        Assert.Contains((201, typeof(UserDto)), produces);
    }

    [Fact]
    public void TypedAsyncResponse_DocumentsTheSuccess()
    {
        var produces = Produces(app => app.MapDelete("/users/{id}", (int id) => Task.FromResult(Result.Success()).ToHttpResultAsync()));

        Assert.Contains(produces, metadata => metadata.Status == 204);
    }

    [Fact]
    public void UntypedResponse_DocumentsNothing()
    {
        var produces = Produces(app => app.MapGet("/users/{id}", (int id) => Found.ToHttpResult(user => (IResult)TypedResults.Ok(user))));

        Assert.Empty(produces);
    }

    [Fact]
    public void ProducesErrors_DocumentsOneProblemPerStatus()
    {
        var produces = Produces(app => app
            .MapPost("/users", () => Found.ToHttpResult())
            .ProducesErrors(ErrorType.Validation, ErrorType.Conflict, ErrorType.Concurrency, ErrorType.TooManyRequests));

        Assert.Contains((200, typeof(UserDto)), produces);
        Assert.Contains((400, typeof(HttpValidationProblemDetails)), produces);
        Assert.Contains((409, typeof(ProblemDetails)), produces);
        Assert.Contains((429, typeof(ProblemDetails)), produces);
        Assert.Single(produces, metadata => metadata.Status == 409);       // Conflict and Concurrency share 409
    }

    [Fact]
    public void ProducesErrors_WorksOnRouteGroups()
    {
        var produces = Produces(app =>
        {
            var users = app.MapGroup("/users").ProducesErrors(ErrorType.NotFound);
            users.MapGet("/{id}", (int id) => Found.ToHttpResult());
        });

        Assert.Contains((404, typeof(ProblemDetails)), produces);
    }

    [Fact]
    public void ProducesErrors_NullArguments_Throw()
    {
        var app = WebApplication.CreateSlimBuilder().Build();
        var endpoint = app.MapGet("/", () => Found.ToHttpResult());

        Assert.Throws<ArgumentNullException>(() => ((RouteHandlerBuilder)null!).ProducesErrors(ErrorType.NotFound));
        Assert.Throws<ArgumentNullException>(() => endpoint.ProducesErrors(null!));
    }
}
