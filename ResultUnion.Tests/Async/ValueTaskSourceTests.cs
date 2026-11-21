using System.Threading.Tasks.Sources;

namespace ResultUnion.Tests.Async;

/// <summary>
/// The pipeline on ValueTask sources: the same outcomes as on Task sources, completed or pending, and every source is
/// awaited exactly once.
/// </summary>
public sealed class ValueTaskSourceTests
{
    private static readonly Error NotFound = Error.NotFound("Missing.");
    private static readonly Error Invalid = Error.Validation("Invalid.");

    private static ValueTask<Result<int>> Five() => ValueTask.FromResult(Result<int>.Success(5));
    private static ValueTask<Result<int>> Missing() => ValueTask.FromResult(Result<int>.Failure(NotFound));
    private static ValueTask<Result> Ok() => ValueTask.FromResult(Result.Success());
    private static ValueTask<Result> Failed() => ValueTask.FromResult(Result.Failure(NotFound));

    private static ValueTask<Result<int>> FiveLater() => new(Later(Result<int>.Success(5)));
    private static ValueTask<Result> FailedLater() => new(Later(Result.Failure(NotFound)));

    private static async Task<T> Later<T>(T value)
    {
        await Task.Yield();
        return value;
    }

    // ---- Result<T> sources -------------------------------------------------------------------------------------------

    [Fact]
    public async Task Value_steps_work_on_completed_and_pending_sources()
    {
        Assert.Equal(10, (await Five().MapAsync(n => n * 2)).ResultValue);
        Assert.Equal(10, (await FiveLater().MapAsync(n => n * 2)).ResultValue);
        Assert.Equal(8, (await Five().MapAsync(n => Task.FromResult(n + 3))).ResultValue);
        Assert.Equal(6, (await FiveLater().BindAsync(n => Result<int>.Success(n + 1))).ResultValue);
        Assert.Equal(7, (await Five().BindAsync(n => Task.FromResult(Result<int>.Success(n + 2)))).ResultValue);
        Assert.True((await Five().BindAsync(_ => Result.Success())).IsSuccess);
        Assert.Same(Invalid, (await Five().BindAsync(_ => Task.FromResult(Result.Failure(Invalid)))).Error);
        Assert.Same(NotFound, (await Missing().MapAsync(n => n * 2)).Error);
    }

    [Fact]
    public async Task Ensure_MapError_and_Match_work_on_ValueTask_sources()
    {
        Assert.Same(Invalid, (await Five().EnsureAsync(n => n > 10, Invalid)).Error);
        Assert.Equal(5, (await FiveLater().EnsureAsync(n => Task.FromResult(n > 1), Invalid)).ResultValue);
        Assert.Same(Invalid, (await Five().EnsureAsync(_ => Result.Failure(Invalid))).Error);
        Assert.Same(Invalid, (await Missing().MapErrorAsync(_ => Invalid)).Error);
        Assert.Equal("5", await Five().MatchAsync(n => n.ToString(), e => e.Code));
        Assert.Equal(NotFound.Code, await Missing().MatchAsync(n => Task.FromResult(n.ToString()), e => Task.FromResult(e.Code)));
    }

    [Fact]
    public async Task Side_effects_run_on_the_matching_track_only()
    {
        var calls = new List<string>();

        _ = await Five().TapAsync(n => calls.Add($"tap:{n}"));
        _ = await FiveLater().TapAsync(async n => { await Task.Yield(); calls.Add($"tapAsync:{n}"); });
        _ = await Missing().TapErrorAsync(e => calls.Add($"tapError:{e.Code}"));
        _ = await Missing().TapAsync(_ => calls.Add("must not run"));
        await Five().MatchAsync(n => calls.Add($"match:{n}"), _ => calls.Add("must not run"));

        Assert.Equal(["tap:5", "tapAsync:5", $"tapError:{NotFound.Code}", "match:5"], calls);
    }

    // ---- Result sources ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Steps_on_a_result_without_value_work_on_ValueTask_sources()
    {
        Assert.True((await Ok().BindAsync(() => Result.Success())).IsSuccess);
        Assert.Equal(3, (await Ok().BindAsync(() => Result<int>.Success(3))).ResultValue);
        Assert.Same(NotFound, (await FailedLater().BindAsync(() => Task.FromResult(Result.Success()))).Error);
        Assert.Same(Invalid, (await Ok().EnsureAsync(() => false, Invalid)).Error);
        Assert.Same(Invalid, (await Failed().MapErrorAsync(_ => Invalid)).Error);
        Assert.Equal("ok", await Ok().MatchAsync(() => "ok", e => e.Code));
    }

    // ---- ToResultAsync on ValueTask<T?> ------------------------------------------------------------------------------

    [Fact]
    public async Task ToResultAsync_turns_a_ValueTask_lookup_into_a_result()
    {
        Assert.Equal("ann", (await ValueTask.FromResult<string?>("ann").ToResultAsync(NotFound)).ResultValue);
        Assert.Same(NotFound, (await ValueTask.FromResult<string?>(null).ToResultAsync(NotFound)).Error);
        Assert.Equal(7, (await new ValueTask<int?>(Later<int?>(7)).ToResultAsync(NotFound)).ResultValue);
        Assert.Same(NotFound, (await ValueTask.FromResult<int?>(null).ToResultAsync(NotFound)).Error);
    }

    // ---- contract ----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Each_source_is_read_exactly_once()
    {
        var source = new CountingSource<Result<int>>(Result<int>.Success(5));
        var lookup = new CountingSource<string?>("ann");

        _ = await source.AsValueTask().MapAsync(n => n * 2);
        _ = await lookup.AsValueTask().ToResultAsync(NotFound);

        Assert.Equal(1, source.Reads);
        Assert.Equal(1, lookup.Reads);
    }

    [Fact]
    public void A_null_delegate_throws_when_the_method_is_called()
    {
        Assert.Throws<ArgumentNullException>(() => { _ = Five().MapAsync((Func<int, int>)null!); });
        Assert.Throws<ArgumentNullException>(() => { _ = Ok().BindAsync((Func<Result>)null!); });
        Assert.Throws<ArgumentNullException>(() => { _ = ValueTask.FromResult<string?>("x").ToResultAsync(null!); });
    }

    /// <summary>A completed IValueTaskSource that counts how often its result is read; a pooled source would break on two reads.</summary>
    private sealed class CountingSource<T>(T value) : IValueTaskSource<T>
    {
        public int Reads { get; private set; }

        public ValueTask<T> AsValueTask() => new(this, token: 0);

        public T GetResult(short token)
        {
            Reads++;
            return value;
        }

        public ValueTaskSourceStatus GetStatus(short token) => ValueTaskSourceStatus.Succeeded;

        public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags) =>
            continuation(state);
    }
}
