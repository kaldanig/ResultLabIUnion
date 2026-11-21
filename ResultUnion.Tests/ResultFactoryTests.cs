namespace ResultUnion.Tests;

/// <summary><see cref="IResultFactory{TSelf}"/>: generic code that creates failures of a result type it only knows as a type parameter.</summary>
public class ResultFactoryTests
{
    private static readonly Error Forbidden = Error.Forbidden("Admins only.");

    private static TResult Fail<TResult>(Error error)
        where TResult : IResultFactory<TResult>
        => TResult.Failure(error);

    // A pipeline-behavior-like wrapper: short-circuits with a failure, or runs the handler.
    private static TResponse RequireAdmin<TResponse>(string role, Func<TResponse> handler)
        where TResponse : IResultFactory<TResponse>
        => role == "admin" ? handler() : TResponse.Failure(Forbidden);

    // A transaction-like wrapper: reads the handler's outcome through IResultState.
    private static TResponse InTransaction<TResponse>(Func<TResponse> handler, List<string> log)
        where TResponse : IResultFactory<TResponse>
    {
        var response = handler();
        log.Add(response.IsSuccess ? "commit" : $"rollback:{response.Error.Code}");
        return response;
    }

    [Fact]
    public void Failure_CreatesFailureOfEachResultType()
    {
        Assert.Same(Forbidden, Fail<Result>(Forbidden).Error);
        Assert.Same(Forbidden, Fail<Result<int>>(Forbidden).Error);
        Assert.Same(Forbidden, Fail<Result<string>>(Forbidden).Error);
    }

    [Fact]
    public void Failure_NullError_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => Fail<Result<int>>(null!));
        Assert.Throws<ArgumentNullException>(() => Fail<Result>(null!));
    }

    [Fact]
    public void Behavior_ShortCircuitsWithoutCallingTheHandler()
    {
        var called = false;

        Result<int> denied = RequireAdmin("guest", () => { called = true; return Result<int>.Success(1); });

        Assert.Same(Forbidden, denied.Error);
        Assert.False(called);
    }

    [Fact]
    public void Behavior_PassesTheHandlerResponseThrough()
    {
        Assert.Equal(42, RequireAdmin("admin", () => Result<int>.Success(42)).ResultValue);
        Assert.True(RequireAdmin("admin", Result.Success).IsSuccess);
    }

    [Fact]
    public void Behavior_CanReadTheOutcomeOfAnyResultType()
    {
        var log = new List<string>();

        _ = InTransaction(() => Result<string>.Success("ok"), log);
        _ = InTransaction(() => Result.Failure(Error.Conflict("Taken.")), log);

        Assert.Equal(["commit", "rollback:conflict"], log);
    }

    [Fact]
    public async Task Behavior_WorksAsynchronously()
    {
        static async Task<TResponse> RequireAdminAsync<TResponse>(string role, Func<Task<TResponse>> handler)
            where TResponse : IResultFactory<TResponse>
            => role == "admin" ? await handler() : TResponse.Failure(Forbidden);

        Assert.Same(Forbidden, (await RequireAdminAsync("guest", () => Task.FromResult(Result<int>.Success(1)))).Error);
        Assert.Equal(1, (await RequireAdminAsync("admin", () => Task.FromResult(Result<int>.Success(1)))).ResultValue);
    }
}
