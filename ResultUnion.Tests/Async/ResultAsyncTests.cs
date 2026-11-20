using static ResultUnion.Tests.Async.EagerAssert;

namespace ResultUnion.Tests.Async;

/// <summary>Async operations on <see cref="Result"/> (taking an asynchronous delegate) and on <see cref="Task{TResult}"/> of <see cref="Result"/>.</summary>
public class ResultAsyncTests
{
    private static readonly Error NotFound = Error.NotFound("Item was not found.");
    private static readonly Error Invalid = Error.Validation("Value is invalid.");

    private static readonly Result Ok = Result.Success();
    private static readonly Result Failed = Result.Failure(NotFound);

    private static async Task<TValue> Later<TValue>(TValue value)
    {
        await Task.Yield();
        return value;
    }

    private static Task<Result> OkLater() => Later(Result.Success());

    private static Task<Result> FailedLater() => Later(Result.Failure(NotFound));

    private static Func<Task<TOut>> NotCalled<TOut>() => () => throw new InvalidOperationException("Delegate must not run.");

    // Result + asynchronous delegate

    [Fact]
    public async Task BindAsync_ChainsOnSuccessOnly()
    {
        Assert.Same(Invalid, (await Ok.BindAsync(() => Later(Result.Failure(Invalid)))).Error);
        Assert.Equal(3, (await Ok.BindAsync(() => Later(Result<int>.Success(3)))).ResultValue);
        Assert.Same(NotFound, (await Failed.BindAsync(NotCalled<Result>())).Error);
        Assert.Same(NotFound, (await Failed.BindAsync(NotCalled<Result<int>>())).Error);
    }

    [Fact]
    public async Task EnsureAsync_PredicateWithErrorOrFactory()
    {
        var factoryCalls = 0;
        Func<Error> factory = () => { factoryCalls++; return Invalid; };

        Assert.True((await Ok.EnsureAsync(() => Later(true), Invalid)).IsSuccess);
        Assert.Same(Invalid, (await Ok.EnsureAsync(() => Later(false), Invalid)).Error);
        Assert.True((await Ok.EnsureAsync(() => Later(true), factory)).IsSuccess);
        Assert.Same(Invalid, (await Ok.EnsureAsync(() => Later(false), factory)).Error);
        Assert.Same(NotFound, (await Failed.EnsureAsync(NotCalled<bool>(), Invalid)).Error);
        Assert.Same(NotFound, (await Failed.EnsureAsync(NotCalled<bool>(), factory)).Error);
        Assert.Equal(1, factoryCalls);
    }

    [Fact]
    public async Task MapErrorAsync_TransformsOnlyFailures()
    {
        Assert.True((await Ok.MapErrorAsync(_ => throw new InvalidOperationException())).IsSuccess);
        Assert.Same(Invalid, (await Failed.MapErrorAsync(_ => Later(Invalid))).Error);
    }

    [Fact]
    public async Task TapAsync_And_TapErrorAsync_AwaitOnlyMatchingSideEffect()
    {
        var calls = new List<string>();

        Assert.Equal(Ok, await Ok.TapAsync(async () => { await Task.Yield(); calls.Add("tap"); }));
        Assert.Equal(Ok, await Ok.TapErrorAsync(async _ => { await Task.Yield(); calls.Add("tapError"); }));
        Assert.Equal(Failed, await Failed.TapAsync(async () => { await Task.Yield(); calls.Add("tap"); }));
        Assert.Equal(Failed, await Failed.TapErrorAsync(async e => { await Task.Yield(); calls.Add($"tapError:{e.Code}"); }));

        Assert.Equal(["tap", "tapError:resource.not_found"], calls);
    }

    [Fact]
    public async Task MatchAsync_AwaitsMatchingBranch()
    {
        Assert.Equal("ok", await Ok.MatchAsync(() => Later("ok"), e => Later(e.Code)));
        Assert.Equal("resource.not_found", await Failed.MatchAsync(() => Later("ok"), e => Later(e.Code)));

        var calls = new List<string>();
        await Ok.MatchAsync(async () => { await Task.Yield(); calls.Add("ok"); }, async e => { await Task.Yield(); calls.Add(e.Code); });
        await Failed.MatchAsync(async () => { await Task.Yield(); calls.Add("ok"); }, async e => { await Task.Yield(); calls.Add(e.Code); });
        Assert.Equal(["ok", "resource.not_found"], calls);
    }

    [Fact]
    public async Task Default_PropagatesDefaultResultError()
    {
        Assert.Same(Error.DefaultResult, (await default(Result).BindAsync(NotCalled<Result>())).Error);
    }

    // Task<Result> + synchronous or asynchronous delegate

    [Fact]
    public async Task TaskSource_BindAsync_AllOverloads()
    {
        Assert.Same(Invalid, (await OkLater().BindAsync(() => Result.Failure(Invalid))).Error);
        Assert.Same(Invalid, (await OkLater().BindAsync(() => Later(Result.Failure(Invalid)))).Error);
        Assert.Equal(1, (await OkLater().BindAsync(() => Result<int>.Success(1))).ResultValue);
        Assert.Equal(2, (await OkLater().BindAsync(() => Later(Result<int>.Success(2)))).ResultValue);
        Assert.Same(NotFound, (await FailedLater().BindAsync(() => Result.Success())).Error);
        Assert.Same(NotFound, (await FailedLater().BindAsync(() => Later(Result<int>.Success(2)))).Error);
    }

    [Fact]
    public async Task TaskSource_EnsureAsync_AllOverloads()
    {
        Assert.True((await OkLater().EnsureAsync(() => true, Invalid)).IsSuccess);
        Assert.Same(Invalid, (await OkLater().EnsureAsync(() => Later(false), Invalid)).Error);
        Assert.Same(Invalid, (await OkLater().EnsureAsync(() => false, () => Invalid)).Error);
        Assert.True((await OkLater().EnsureAsync(() => Later(true), () => Invalid)).IsSuccess);
        Assert.Same(NotFound, (await FailedLater().EnsureAsync(() => false, Invalid)).Error);
    }

    [Fact]
    public async Task TaskSource_MapErrorAsync_SyncAndAsyncMapper()
    {
        Assert.Same(Invalid, (await FailedLater().MapErrorAsync(_ => Invalid)).Error);
        Assert.Same(Invalid, (await FailedLater().MapErrorAsync(_ => Later(Invalid))).Error);
        Assert.True((await OkLater().MapErrorAsync(_ => Invalid)).IsSuccess);
    }

    [Fact]
    public async Task TaskSource_TapAsync_And_TapErrorAsync_SyncAndAsyncAction()
    {
        var calls = new List<string>();

        await OkLater().TapAsync(() => calls.Add("tap"));
        await OkLater().TapAsync(async () => { await Task.Yield(); calls.Add("tapAsync"); });
        await FailedLater().TapErrorAsync(e => calls.Add($"tapError:{e.Code}"));
        await FailedLater().TapErrorAsync(async e => { await Task.Yield(); calls.Add($"tapErrorAsync:{e.Code}"); });
        await FailedLater().TapAsync(() => calls.Add("must not run"));
        await OkLater().TapErrorAsync(_ => calls.Add("must not run"));

        Assert.Equal(["tap", "tapAsync", "tapError:resource.not_found", "tapErrorAsync:resource.not_found"], calls);
    }

    [Fact]
    public async Task TaskSource_MatchAsync_AllOverloads()
    {
        Assert.Equal("ok", await OkLater().MatchAsync(() => "ok", e => e.Code));
        Assert.Equal("resource.not_found", await FailedLater().MatchAsync(() => Later("ok"), e => Later(e.Code)));

        var calls = new List<string>();
        await OkLater().MatchAsync(() => calls.Add("ok"), e => calls.Add(e.Code));
        await FailedLater().MatchAsync(async () => { await Task.Yield(); calls.Add("ok"); }, async e => { await Task.Yield(); calls.Add(e.Code); });
        Assert.Equal(["ok", "resource.not_found"], calls);
    }

    // Overload resolution

    [Fact]
    public async Task OverloadResolution_TaskReturningDelegates_PickAsyncOverloads()
    {
        // Explicit types prove the choice at compile time.
        Task<Result<int>> bound = OkLater().BindAsync(async () => { await Task.Yield(); return Result<int>.Success(4); });
        Task<string> matched = OkLater().MatchAsync(() => Later("ok"), _ => Later("error"));
        Task matchedWithoutValue = OkLater().MatchAsync(() => Task.CompletedTask, _ => Task.CompletedTask);

        Assert.Equal(4, (await bound).ResultValue);
        Assert.Equal("ok", await matched);
        await matchedWithoutValue;
    }

    [Fact]
    public async Task OverloadResolution_AsyncSideEffect_IsAwaited_NotFiredAndForgotten()
    {
        var gate = new TaskCompletionSource();
        var done = false;

        var pending = OkLater().TapAsync(async () => { await gate.Task; done = true; });
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
        Task<Result> nullSource = null!;

        ThrowsEagerly(() => _ = nullSource.BindAsync(() => Result.Success()));
        ThrowsEagerly(() => _ = nullSource.MatchAsync(() => 0, _ => 1));
        ThrowsEagerly(() => _ = Ok.BindAsync((Func<Task<Result>>)null!));
        ThrowsEagerly(() => _ = Failed.BindAsync((Func<Task<Result<int>>>)null!));
        ThrowsEagerly(() => _ = Ok.EnsureAsync((Func<Task<bool>>)null!, Invalid));
        ThrowsEagerly(() => _ = Ok.EnsureAsync(() => Later(true), (Error)null!));
        ThrowsEagerly(() => _ = Ok.MapErrorAsync(null!));
        ThrowsEagerly(() => _ = Ok.TapAsync(null!));
        ThrowsEagerly(() => _ = Ok.TapErrorAsync(null!));
        ThrowsEagerly(() => _ = Ok.MatchAsync((Func<Task>)null!, _ => Task.CompletedTask));
        ThrowsEagerly(() => _ = OkLater().BindAsync((Func<Result>)null!));
        ThrowsEagerly(() => _ = OkLater().EnsureAsync((Func<bool>)null!, Invalid));
        ThrowsEagerly(() => _ = OkLater().TapAsync((Action)null!));
        ThrowsEagerly(() => _ = OkLater().MatchAsync((Action)null!, _ => { }));
    }

    [Fact]
    public async Task DelegateException_SurfacesWhenAwaited()
    {
        Func<Task<Result>> throwing = () => throw new InvalidOperationException("boom");

        var task = Ok.BindAsync(throwing);

        Assert.Equal("boom", (await Assert.ThrowsAsync<InvalidOperationException>(() => task)).Message);
    }

    // End-to-end: a command pipeline that starts with Task<Result> and ends with a value.

    [Theory]
    [InlineData(true, true, "saved:42")]
    [InlineData(false, true, "error:forbidden")]
    [InlineData(true, false, "error:validation.failed")]
    public async Task Pipeline_CommandThenQuery(bool authorised, bool valid, string expected)
    {
        Task<Result> Authorise() => Later(authorised ? Result.Success() : Result.Failure(Error.Forbidden("No access.")));

        var outcome = await Authorise()
            .EnsureAsync(() => Later(valid), Error.Validation("Invalid payload."))
            .TapAsync(() => Task.CompletedTask)
            .BindAsync(() => Later(Result<int>.Success(42)))
            .MatchAsync(id => $"saved:{id}", error => $"error:{error.Code}");

        Assert.Equal(expected, outcome);
    }
}
