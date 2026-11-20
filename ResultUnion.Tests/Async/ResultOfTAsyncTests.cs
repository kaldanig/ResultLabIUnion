using static ResultUnion.Tests.Async.EagerAssert;

namespace ResultUnion.Tests.Async;

/// <summary>Async operations on a <see cref="Result{T}"/> taking an asynchronous delegate.</summary>
public class ResultOfTAsyncTests
{
    private static readonly Error NotFound = Error.NotFound("Item was not found.");
    private static readonly Error Invalid = Error.Validation("Value is invalid.");

    private static readonly Result<int> Five = Result<int>.Success(5);
    private static readonly Result<int> Missing = Result<int>.Failure(NotFound);

    private static async Task<TValue> Later<TValue>(TValue value)
    {
        await Task.Yield();
        return value;
    }

    private static Func<TIn, Task<TOut>> NotCalled<TIn, TOut>() => _ => throw new InvalidOperationException("Delegate must not run.");

    // MapAsync

    [Fact]
    public async Task MapAsync_Success_AwaitsMapper()
    {
        Assert.Equal("#10", (await Five.MapAsync(n => Later($"#{n * 2}"))).ResultValue);
    }

    [Fact]
    public async Task MapAsync_Failure_PropagatesErrorWithoutCallingMapper()
    {
        Assert.Same(NotFound, (await Missing.MapAsync(NotCalled<int, string>())).Error);
    }

    // BindAsync

    [Fact]
    public async Task BindAsync_Generic_ChainsOnSuccessOnly()
    {
        Assert.Equal(6, (await Five.BindAsync(n => Later(Result<int>.Success(n + 1)))).ResultValue);
        Assert.Same(Invalid, (await Five.BindAsync(_ => Later(Result<int>.Failure(Invalid)))).Error);
        Assert.Same(NotFound, (await Missing.BindAsync(NotCalled<int, Result<int>>())).Error);
    }

    [Fact]
    public async Task BindAsync_NonGeneric_ChainsOnSuccessOnly()
    {
        Assert.True((await Five.BindAsync(_ => Later(Result.Success()))).IsSuccess);
        Assert.Same(Invalid, (await Five.BindAsync(_ => Later(Result.Failure(Invalid)))).Error);
        Assert.Same(NotFound, (await Missing.BindAsync(NotCalled<int, Result>())).Error);
    }

    // EnsureAsync

    [Fact]
    public async Task EnsureAsync_PredicateAndError()
    {
        Assert.Equal(Five, await Five.EnsureAsync(n => Later(n > 0), Invalid));
        Assert.Same(Invalid, (await Five.EnsureAsync(n => Later(n > 10), Invalid)).Error);
        Assert.Same(NotFound, (await Missing.EnsureAsync(NotCalled<int, bool>(), Invalid)).Error);
    }

    [Fact]
    public async Task EnsureAsync_PredicateAndErrorFactory()
    {
        var calls = 0;
        Func<int, Error> factory = n => { calls++; return Error.Validation($"{n} is too small."); };

        Assert.Equal(Five, await Five.EnsureAsync(n => Later(n > 0), factory));
        Assert.Equal(0, calls);
        Assert.Equal("5 is too small.", (await Five.EnsureAsync(n => Later(n > 10), factory)).Error.Message);
        Assert.Same(NotFound, (await Missing.EnsureAsync(NotCalled<int, bool>(), factory)).Error);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task EnsureAsync_Check()
    {
        Assert.Equal(Five, await Five.EnsureAsync(_ => Later(Result.Success())));
        Assert.Same(Invalid, (await Five.EnsureAsync(_ => Later(Result.Failure(Invalid)))).Error);
        Assert.Same(NotFound, (await Missing.EnsureAsync(NotCalled<int, Result>())).Error);
    }

    // MapErrorAsync

    [Fact]
    public async Task MapErrorAsync_TransformsOnlyFailures()
    {
        Assert.Equal(Five, await Five.MapErrorAsync(NotCalled<Error, Error>()));
        Assert.Same(Invalid, (await Missing.MapErrorAsync(_ => Later(Invalid))).Error);
    }

    // TapAsync / TapErrorAsync

    [Fact]
    public async Task TapAsync_And_TapErrorAsync_AwaitOnlyMatchingSideEffect()
    {
        var calls = new List<string>();

        Assert.Equal(Five, await Five.TapAsync(async n => { await Task.Yield(); calls.Add($"tap:{n}"); }));
        Assert.Equal(Five, await Five.TapErrorAsync(async _ => { await Task.Yield(); calls.Add("tapError"); }));
        Assert.Equal(Missing, await Missing.TapAsync(async _ => { await Task.Yield(); calls.Add("tap"); }));
        Assert.Equal(Missing, await Missing.TapErrorAsync(async e => { await Task.Yield(); calls.Add($"tapError:{e.Code}"); }));

        Assert.Equal(["tap:5", "tapError:resource.not_found"], calls);
    }

    // MatchAsync

    [Fact]
    public async Task MatchAsync_WithValue_AwaitsMatchingBranch()
    {
        Assert.Equal("ok:5", await Five.MatchAsync(n => Later($"ok:{n}"), e => Later($"error:{e.Code}")));
        Assert.Equal("error:resource.not_found", await Missing.MatchAsync(n => Later($"ok:{n}"), e => Later($"error:{e.Code}")));
    }

    [Fact]
    public async Task MatchAsync_WithoutValue_AwaitsMatchingBranch()
    {
        var calls = new List<string>();

        await Five.MatchAsync(async n => { await Task.Yield(); calls.Add($"ok:{n}"); }, async _ => { await Task.Yield(); calls.Add("error"); });
        await Missing.MatchAsync(async _ => { await Task.Yield(); calls.Add("ok"); }, async e => { await Task.Yield(); calls.Add($"error:{e.Code}"); });

        Assert.Equal(["ok:5", "error:resource.not_found"], calls);
    }

    // default

    [Fact]
    public async Task Default_PropagatesDefaultResultError()
    {
        Assert.Same(Error.DefaultResult, (await default(Result<int>).MapAsync(NotCalled<int, int>())).Error);
        Assert.Same(Error.DefaultResult, (await default(Result<int>).BindAsync(NotCalled<int, Result<int>>())).Error);
    }

    // Argument validation is eager; delegate exceptions surface through the task.

    [Fact]
    public void NullDelegates_ThrowWhenCalled_NotWhenAwaited()
    {
        ThrowsEagerly(() => _ = Five.MapAsync((Func<int, Task<int>>)null!));
        ThrowsEagerly(() => _ = Five.BindAsync((Func<int, Task<Result<int>>>)null!));
        ThrowsEagerly(() => _ = Five.BindAsync((Func<int, Task<Result>>)null!));
        ThrowsEagerly(() => _ = Five.EnsureAsync((Func<int, Task<bool>>)null!, Invalid));
        ThrowsEagerly(() => _ = Five.EnsureAsync(_ => Later(true), (Error)null!));
        ThrowsEagerly(() => _ = Five.EnsureAsync(_ => Later(true), (Func<int, Error>)null!));
        ThrowsEagerly(() => _ = Five.EnsureAsync((Func<int, Task<Result>>)null!));
        ThrowsEagerly(() => _ = Five.MapErrorAsync(null!));
        ThrowsEagerly(() => _ = Five.TapAsync(null!));
        ThrowsEagerly(() => _ = Five.TapErrorAsync(null!));
        ThrowsEagerly(() => _ = Five.MatchAsync(null!, (Func<Error, Task<int>>)(_ => Later(0))));
        ThrowsEagerly(() => _ = Five.MatchAsync((Func<int, Task>)(_ => Task.CompletedTask), null!));
    }

    [Fact]
    public void NullDelegates_ThrowEvenOnTheTrackWhereTheyWouldNotRun()
    {
        ThrowsEagerly(() => _ = Missing.MapAsync((Func<int, Task<int>>)null!));
        ThrowsEagerly(() => _ = Five.TapErrorAsync(null!));
    }

    [Fact]
    public async Task DelegateException_SurfacesWhenAwaited()
    {
        Func<int, Task<int>> throwing = _ => throw new InvalidOperationException("boom");

        var task = Five.MapAsync(throwing);                    // no exception yet: it is stored in the task

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => task);
        Assert.Equal("boom", exception.Message);
    }

    [Fact]
    public async Task MapAsync_MapperReturnsNull_ThrowsWhenAwaited()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => Five.MapAsync(_ => Later<string>(null!)));
    }
}
