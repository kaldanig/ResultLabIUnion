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

    // Responses are typed Results<TSuccess, ProblemHttpResult>; .Result is the response that was chosen.

    [Fact]
    public void ResultOfT_Success_IsOkWithTheValue()
    {
        Results<Ok<UserDto>, ProblemHttpResult> response = Result<UserDto>.Success(new UserDto(1, "ann@example.com")).ToHttpResult();

        Assert.Equal(new UserDto(1, "ann@example.com"), Assert.IsType<Ok<UserDto>>(response.Result).Value);
    }

    [Fact]
    public void ResultOfT_CustomSuccess_IsTypedWithTheGivenResponse()
    {
        Results<Created<UserDto>, ProblemHttpResult> response = Result<UserDto>.Success(new UserDto(7, "ann@example.com"))
            .ToHttpResult(user => TypedResults.Created($"/users/{user.Id}", user));

        Assert.Equal("/users/7", Assert.IsType<Created<UserDto>>(response.Result).Location);
    }

    [Fact]
    public void ResultOfT_SeveralPossibleSuccessResponses_UseTheUntypedOverload()
    {
        // Two different response types: TSuccess cannot be inferred, so the IResult overload is chosen.
        IResult response = Result<UserDto>.Success(new UserDto(7, "ann@example.com"))
            .ToHttpResult(user => user.Id > 5 ? TypedResults.Redirect("/elsewhere") : (IResult)TypedResults.Ok(user));

        Assert.IsType<RedirectHttpResult>(response);
    }

    [Fact]
    public void ResultOfT_Failure_IsProblemWithTheMappedStatus()
    {
        var problem = Assert.IsType<ProblemHttpResult>(Result<UserDto>.Failure(Taken).ToHttpResult(_ => TypedResults.Ok()).Result);

        Assert.Equal(409, problem.StatusCode);
        Assert.Equal("conflict", problem.ProblemDetails.Extensions["code"]);
    }

    [Fact]
    public void ResultOfT_Default_IsProblem500()
    {
        Assert.Equal(500, Assert.IsType<ProblemHttpResult>(default(Result<int>).ToHttpResult().Result).StatusCode);
    }

    [Fact]
    public void Error_IsATypedProblem()
    {
        ProblemHttpResult problem = Taken.ToHttpResult();

        Assert.Equal(409, problem.StatusCode);
    }

    [Fact]
    public async Task The_untyped_overloads_send_the_problem_on_failure_sync_and_async()
    {
        IResult redirect() => TypedResults.Redirect("/elsewhere");

        Assert.IsType<RedirectHttpResult>(Result.Success().ToHttpResult(redirect));
        Assert.Equal(409, Assert.IsType<ProblemHttpResult>(Result.Failure(Taken).ToHttpResult(redirect)).StatusCode);
        Assert.Equal(409, Assert.IsType<ProblemHttpResult>(Result<int>.Failure(Taken).ToHttpResult(_ => redirect())).StatusCode);
        Assert.Throws<ArgumentNullException>(() => Result.Success().ToHttpResult((Func<IResult>)(() => null!)));

        Assert.IsType<RedirectHttpResult>(await Task.FromResult(Result<int>.Success(1)).ToHttpResultAsync(_ => redirect()));
        Assert.IsType<RedirectHttpResult>(await Task.FromResult(Result.Success()).ToHttpResultAsync(redirect));
        Assert.IsType<ProblemHttpResult>(await Task.FromResult(Result<int>.Failure(Taken)).ToHttpResultAsync(_ => redirect()));
        Assert.IsType<ProblemHttpResult>(await Task.FromResult(Result.Failure(Taken)).ToHttpResultAsync(redirect));
    }

    // Result

    [Fact]
    public void Result_SuccessIsNoContent_FailureIsProblem()
    {
        Results<NoContent, ProblemHttpResult> noContent = Result.Success().ToHttpResult();
        Results<Accepted, ProblemHttpResult> accepted = Result.Success().ToHttpResult(() => TypedResults.Accepted((string?)null));

        Assert.IsType<NoContent>(noContent.Result);
        Assert.IsType<Accepted>(accepted.Result);
        Assert.Equal(409, Assert.IsType<ProblemHttpResult>(Result.Failure(Taken).ToHttpResult().Result).StatusCode);
    }

    // Async

    [Fact]
    public async Task Async_AwaitsTheResultFirst()
    {
        Results<Ok<int>, ProblemHttpResult> ok = await Task.FromResult(Result<int>.Success(1)).ToHttpResultAsync();
        Results<Created<int>, ProblemHttpResult> created = await Task.FromResult(Result<int>.Success(1)).ToHttpResultAsync(id => TypedResults.Created($"/x/{id}", id));
        Results<NoContent, ProblemHttpResult> noContent = await Task.FromResult(Result.Success()).ToHttpResultAsync();
        Results<Ok, ProblemHttpResult> problem = await Task.FromResult(Result.Failure(Taken)).ToHttpResultAsync(() => TypedResults.Ok());

        Assert.IsType<Ok<int>>(ok.Result);
        Assert.IsType<Created<int>>(created.Result);
        Assert.IsType<NoContent>(noContent.Result);
        Assert.IsType<ProblemHttpResult>(problem.Result);
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
    public async Task Wire_ArgumentsAreSent_AndOmittedWhenEmpty()
    {
        var error = Error.Validation(
        [
            Error.Create("password.too_short", "Too short.", ErrorType.Validation).WithArgument("minimumLength", 8).WithField("password"),
            Error.Create("email.required", "Required.", ErrorType.Validation).WithField("email"),
        ]);

        var (_, _, body) = await SendAsync(error.ToHttpResult());
        var details = body!.Value.GetProperty("details");

        Assert.Equal(8, details[0].GetProperty("arguments").GetProperty("minimumLength").GetInt32());
        Assert.False(details[1].TryGetProperty("arguments", out _));

        var (_, _, single) = await SendAsync(Error.BusinessRule("Too young.").WithArgument("minimumAge", 18).ToHttpResult());
        Assert.Equal(18, single!.Value.GetProperty("arguments").GetProperty("minimumAge").GetInt32());
    }

    [Fact]
    public async Task Wire_NonValidationErrorWithField_HasTheTitleOfItsStatus()
    {
        // Found through the Web API demo: a NotFound with a field used to get the title "One or more validation errors occurred.".
        var error = Error.NotFound("No user with email zoe@example.com.").WithField("customerEmail");

        var (status, _, body) = await SendAsync(error.ToHttpResult());

        Assert.Equal(404, status);
        Assert.Equal("Not Found", body!.Value.GetProperty("title").GetString());
        Assert.Equal("No user with email zoe@example.com.", body.Value.GetProperty("errors").GetProperty("customerEmail")[0].GetString());

        var (_, _, validation) = await SendAsync(Error.Validation("Bad.").WithField("email").ToHttpResult());
        Assert.Equal("One or more validation errors occurred.", validation!.Value.GetProperty("title").GetString());
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
