using static ResultUnion.Tests.Async.EagerAssert;

namespace ResultUnion.Tests.Async;

/// <summary>Async operations on a <see cref="Task{TResult}"/> of <see cref="Result{T}"/>, so a pipeline continues after <c>await</c>.</summary>
public class TaskOfResultOfTAsyncTests
{
    private static readonly Error NotFound = Error.NotFound("Item was not found.");
    private static readonly Error Invalid = Error.Validation("Value is invalid.");

    private static async Task<TValue> Later<TValue>(TValue value)
    {
        await Task.Yield();
        return value;
    }

    private static Task<Result<int>> Five() => Later(Result<int>.Success(5));

    private static Task<Result<int>> Missing() => Later(Result<int>.Failure(NotFound));

    // Each operator, with a synchronous and an asynchronous delegate.

    [Fact]
    public async Task MapAsync_SyncAndAsyncMapper()
    {
        Assert.Equal(6, (await Five().MapAsync(n => n + 1)).ResultValue);
        Assert.Equal(7, (await Five().MapAsync(n => Later(n + 2))).ResultValue);
        Assert.Same(NotFound, (await Missing().MapAsync(n => n + 1)).Error);
        Assert.Same(NotFound, (await Missing().MapAsync(n => Later(n + 2))).Error);
    }

    [Fact]
    public async Task BindAsync_SyncAndAsyncNext()
    {
        Assert.Equal(6, (await Five().BindAsync(n => Result<int>.Success(n + 1))).ResultValue);
        Assert.Equal(7, (await Five().BindAsync(n => Later(Result<int>.Success(n + 2)))).ResultValue);
        Assert.True((await Five().BindAsync(_ => Result.Success())).IsSuccess);
        Assert.Same(Invalid, (await Five().BindAsync(_ => Later(Result.Failure(Invalid)))).Error);
        Assert.Same(NotFound, (await Missing().BindAsync(n => Result<int>.Success(n))).Error);
        Assert.Same(NotFound, (await Missing().BindAsync(_ => Later(Result.Success()))).Error);
    }

    [Fact]
    public async Task EnsureAsync_AllOverloads()
    {
        Func<int, Error> factory = n => Error.Validation($"{n} is too small.");

        Assert.True((await Five().EnsureAsync(n => n > 0, Invalid)).IsSuccess);
        Assert.Same(Invalid, (await Five().EnsureAsync(n => Later(n > 10), Invalid)).Error);
        Assert.Equal("5 is too small.", (await Five().EnsureAsync(n => n > 10, factory)).Error.Message);
        Assert.True((await Five().EnsureAsync(n => Later(n > 0), factory)).IsSuccess);
        Assert.Same(Invalid, (await Five().EnsureAsync(_ => Result.Failure(Invalid))).Error);
        Assert.True((await Five().EnsureAsync(_ => Later(Result.Success()))).IsSuccess);
        Assert.Same(NotFound, (await Missing().EnsureAsync(n => n > 0, Invalid)).Error);
    }

    [Fact]
    public async Task MapErrorAsync_SyncAndAsyncMapper()
    {
        Assert.Same(Invalid, (await Missing().MapErrorAsync(_ => Invalid)).Error);
        Assert.Same(Invalid, (await Missing().MapErrorAsync(_ => Later(Invalid))).Error);
        Assert.Equal(5, (await Five().MapErrorAsync(_ => Invalid)).ResultValue);
    }

    [Fact]
    public async Task TapAsync_And_TapErrorAsync_SyncAndAsyncAction()
    {
        var calls = new List<string>();

        await Five().TapAsync(n => calls.Add($"tap:{n}"));
        await Five().TapAsync(async n => { await Task.Yield(); calls.Add($"tapAsync:{n}"); });
        await Missing().TapErrorAsync(e => calls.Add($"tapError:{e.Code}"));
        await Missing().TapErrorAsync(async e => { await Task.Yield(); calls.Add($"tapErrorAsync:{e.Code}"); });
        await Missing().TapAsync(_ => calls.Add("must not run"));
        await Five().TapErrorAsync(_ => calls.Add("must not run"));

        Assert.Equal(["tap:5", "tapAsync:5", "tapError:resource.not_found", "tapErrorAsync:resource.not_found"], calls);
    }

    [Fact]
    public async Task MatchAsync_AllOverloads()
    {
        Assert.Equal("ok:5", await Five().MatchAsync(n => $"ok:{n}", e => e.Code));
        Assert.Equal("resource.not_found", await Missing().MatchAsync(n => Later($"ok:{n}"), e => Later(e.Code)));

        var calls = new List<string>();
        await Five().MatchAsync(n => calls.Add($"ok:{n}"), e => calls.Add(e.Code));
        await Missing().MatchAsync(async n => { await Task.Yield(); calls.Add($"ok:{n}"); }, async e => { await Task.Yield(); calls.Add(e.Code); });
        Assert.Equal(["ok:5", "resource.not_found"], calls);
    }

    // Overload resolution: the asynchronous delegate overload must win for async lambdas and Task-returning calls.
    // Assigning to an explicitly typed variable proves it at compile time: the synchronous overload would
    // produce Task<Result<Task<int>>> instead.

    [Fact]
    public async Task OverloadResolution_TaskReturningDelegates_PickAsyncOverloads()
    {
        Task<Result<int>> asyncLambda = Five().MapAsync(async n => { await Task.Yield(); return n + 1; });
        Task<Result<int>> taskReturningCall = Five().MapAsync(n => Later(n + 1));
        Task<int> matched = Five().MatchAsync(n => Later(n), _ => Later(0));

        Assert.Equal(6, (await asyncLambda).ResultValue);
        Assert.Equal(6, (await taskReturningCall).ResultValue);
        Assert.Equal(5, await matched);
    }

    [Fact]
    public async Task OverloadResolution_AsyncSideEffect_IsAwaited_NotFiredAndForgotten()
    {
        // With the Action<T> overload the lambda would become 'async void' and the returned task would complete at once.
        var gate = new TaskCompletionSource();
        var done = false;

        var pending = Five().TapAsync(async _ => { await gate.Task; done = true; });
        await Task.Yield();

        Assert.False(pending.IsCompleted);
        gate.SetResult();
        await pending;
        Assert.True(done);
    }

    [Fact]
    public async Task OverloadResolution_AsyncMatchBranches_AreAwaited()
    {
        var gate = new TaskCompletionSource();
        var done = false;

        var pending = Missing().MatchAsync(_ => Task.CompletedTask, async _ => { await gate.Task; done = true; });
        await Task.Yield();

        Assert.False(pending.IsCompleted);
        gate.SetResult();
        await pending;
        Assert.True(done);
    }

    // Argument validation is eager.

    [Fact]
    public void NullSourceOrDelegate_ThrowsWhenCalled()
    {
        Task<Result<int>> nullSource = null!;

        ThrowsEagerly(() => _ = nullSource.MapAsync(n => n));
        ThrowsEagerly(() => _ = nullSource.BindAsync(n => Result<int>.Success(n)));
        ThrowsEagerly(() => _ = nullSource.MatchAsync(n => n, _ => 0));
        ThrowsEagerly(() => _ = Five().MapAsync((Func<int, int>)null!));
        ThrowsEagerly(() => _ = Five().MapAsync((Func<int, Task<int>>)null!));
        ThrowsEagerly(() => _ = Five().BindAsync((Func<int, Result>)null!));
        ThrowsEagerly(() => _ = Five().EnsureAsync((Func<int, bool>)null!, Invalid));
        ThrowsEagerly(() => _ = Five().EnsureAsync(_ => true, (Error)null!));
        ThrowsEagerly(() => _ = Five().MapErrorAsync((Func<Error, Error>)null!));
        ThrowsEagerly(() => _ = Five().TapAsync((Action<int>)null!));
        ThrowsEagerly(() => _ = Five().TapErrorAsync((Func<Error, Task>)null!));
        ThrowsEagerly(() => _ = Five().MatchAsync((Action<int>)null!, _ => { }));
    }

    [Fact]
    public async Task FaultedSource_SurfacesWhenAwaited()
    {
        var faulted = Task.FromException<Result<int>>(new InvalidOperationException("database down"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => faulted.MapAsync(n => n + 1));
        Assert.Equal("database down", exception.Message);
    }

    // End-to-end: sync and async steps mixed in one pipeline after the first await.

    private static Task<Result<int>> FindAgeAsync(string name) =>
        name switch
        {
            "ann" => Later(Result<int>.Success(34)),
            "bob" => Later(Result<int>.Success(15)),
            "max" => Later(Result<int>.Success(99)),
            _ => Later(Result<int>.Failure(Error.NotFound($"No user '{name}'."))),
        };

    private static Task<Result> EnsureNotBannedAsync(int age) =>
        Later(age == 99 ? Result.Failure(Error.Forbidden("Banned.")) : Result.Success());

    [Theory]
    [InlineData("ann", "ok:35")]
    [InlineData("bob", "error:business_rule.violated")]
    [InlineData("max", "error:forbidden")]
    [InlineData("zoe", "error:resource.not_found")]
    public async Task Pipeline_MixesSyncAndAsyncSteps(string name, string expected)
    {
        var log = new List<string>();

        var outcome = await FindAgeAsync(name)
            .EnsureAsync(age => age >= 18, Error.BusinessRule("Too young."))
            .EnsureAsync(EnsureNotBannedAsync)
            .MapAsync(age => Later(age + 1))
            .TapAsync(age => log.Add($"age {age}"))
            .TapErrorAsync(error => log.Add($"failed {error.Code}"))
            .MatchAsync(age => $"ok:{age}", error => $"error:{error.Code}");

        Assert.Equal(expected, outcome);
        Assert.Single(log);
    }
}
