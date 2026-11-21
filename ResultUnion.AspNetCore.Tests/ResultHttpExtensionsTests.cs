using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using ResultUnion.AspNetCore;
using static ResultUnion.AspNetCore.Tests.EagerAssert;

namespace ResultUnion.AspNetCore.Tests;

public class ResultHttpExtensionsTests
{
    private static readonly Error Taken = Error.Conflict("Email is taken.");

    private sealed record UserDto(int Id, string Email);

    // Executes the response as ASP.NET Core would, and returns what the client receives.
    private static async Task<(int Status, string? ContentType, JsonElement? Body)> SendAsync(IResult response)
    {
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider(),
        };
        using var body = new MemoryStream();
        context.Response.Body = body;

        await response.ExecuteAsync(context);

        body.Position = 0;
        JsonElement? json = body.Length == 0 ? null : JsonDocument.Parse(body).RootElement.Clone();
        return (context.Response.StatusCode, context.Response.ContentType, json);
    }

    // Result<T>

    [Fact]
    public void ResultOfT_Success_IsOkWithTheValue()
    {
        var ok = Assert.IsType<Ok<UserDto>>(Result<UserDto>.Success(new UserDto(1, "ann@example.com")).ToHttpResult());

        Assert.Equal(new UserDto(1, "ann@example.com"), ok.Value);
    }

    [Fact]
    public void ResultOfT_CustomSuccess_UsesTheGivenResponse()
    {
        var response = Result<UserDto>.Success(new UserDto(7, "ann@example.com"))
            .ToHttpResult(user => TypedResults.Created($"/users/{user.Id}", user));

        Assert.Equal("/users/7", Assert.IsType<Created<UserDto>>(response).Location);
    }

    [Fact]
    public void ResultOfT_Failure_IsProblemWithTheMappedStatus()
    {
        var problem = Assert.IsType<ProblemHttpResult>(Result<UserDto>.Failure(Taken).ToHttpResult(_ => TypedResults.Ok()));

        Assert.Equal(409, problem.StatusCode);
        Assert.Equal("conflict", problem.ProblemDetails.Extensions["code"]);
    }

    [Fact]
    public void ResultOfT_Default_IsProblem500()
    {
        Assert.Equal(500, Assert.IsType<ProblemHttpResult>(default(Result<int>).ToHttpResult()).StatusCode);
    }

    // Result

    [Fact]
    public void Result_SuccessIsNoContent_FailureIsProblem()
    {
        Assert.IsType<NoContent>(Result.Success().ToHttpResult());
        Assert.IsType<Accepted>(Result.Success().ToHttpResult(() => TypedResults.Accepted((string?)null)));
        Assert.Equal(409, Assert.IsType<ProblemHttpResult>(Result.Failure(Taken).ToHttpResult()).StatusCode);
    }

    // Async

    [Fact]
    public async Task Async_AwaitsTheResultFirst()
    {
        Assert.IsType<Ok<int>>(await Task.FromResult(Result<int>.Success(1)).ToHttpResultAsync());
        Assert.IsType<Created<int>>(await Task.FromResult(Result<int>.Success(1)).ToHttpResultAsync(id => TypedResults.Created($"/x/{id}", id)));
        Assert.IsType<NoContent>(await Task.FromResult(Result.Success()).ToHttpResultAsync());
        Assert.IsType<ProblemHttpResult>(await Task.FromResult(Result.Failure(Taken)).ToHttpResultAsync(() => TypedResults.Ok()));
    }

    // Argument validation

    [Fact]
    public void NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => Result<int>.Success(1).ToHttpResult(null!));
        Assert.Throws<ArgumentNullException>(() => Result.Success().ToHttpResult(null!));
        Assert.Throws<ArgumentNullException>(() => Result<int>.Success(1).ToHttpResult(_ => null!));
        ThrowsEagerly(() => _ = ((Task<Result<int>>)null!).ToHttpResultAsync());
        ThrowsEagerly(() => _ = Task.FromResult(Result.Success()).ToHttpResultAsync(null!));
    }

    // What the client receives

    [Fact]
    public async Task Wire_ProblemResponse_IsRfc9457JsonWithCode()
    {
        var (status, contentType, body) = await SendAsync(Error.NotFound("No user 7.").ToHttpResult());

        Assert.Equal(404, status);
        Assert.Equal("application/problem+json", contentType);
        Assert.Equal(404, body!.Value.GetProperty("status").GetInt32());
        Assert.Equal("No user 7.", body.Value.GetProperty("detail").GetString());
        Assert.Equal("resource.not_found", body.Value.GetProperty("code").GetString());
        Assert.False(string.IsNullOrEmpty(body.Value.GetProperty("title").GetString()));
        Assert.False(string.IsNullOrEmpty(body.Value.GetProperty("type").GetString()));
    }

    [Fact]
    public async Task Wire_ValidationResponse_HasErrorsPerFieldAndDetailsWithCodes()
    {
        var error = Error.Validation(
        [
            Error.Create("email.invalid_format", "Not an email.", ErrorType.Validation).WithField("email"),
            Error.Create("password.too_short", "Too short.", ErrorType.Validation).WithField("password"),
        ]);

        var (status, _, body) = await SendAsync(Result<int>.Failure(error).ToHttpResult());

        Assert.Equal(400, status);
        Assert.Equal("Not an email.", body!.Value.GetProperty("errors").GetProperty("email")[0].GetString());
        Assert.Equal("Too short.", body.Value.GetProperty("errors").GetProperty("password")[0].GetString());

        var details = body.Value.GetProperty("details");
        Assert.Equal(2, details.GetArrayLength());
        Assert.Equal("password.too_short", details[1].GetProperty("code").GetString());
        Assert.Equal("password", details[1].GetProperty("field").GetString());
    }

    [Fact]
    public async Task Wire_Success_IsJsonValue()
    {
        var (status, contentType, body) = await SendAsync(Result<UserDto>.Success(new UserDto(1, "ann@example.com")).ToHttpResult());

        Assert.Equal(200, status);
        Assert.StartsWith("application/json", contentType);
        Assert.Equal("ann@example.com", body!.Value.GetProperty("email").GetString());
    }
}
