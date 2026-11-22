namespace ResultUnion.Tests.Async;

/// <summary>
/// The paths taken when the source task has already completed (Task.FromResult, a cache hit): the same outcomes as
/// with a pending source, the source task returned when a step leaves the result unchanged, and exceptions delivered
/// through the returned task, never thrown by the call.
/// </summary>
public sealed class CompletedSourceTests
{
    private static readonly Error NotFound = Error.NotFound("Missing.");
    private static readonly Error Invalid = Error.Validation("Invalid.");

    private static Task<Result<int>> Five() => Task.FromResult(Result<int>.Success(5));
    private static Task<Result<int>> Missing() => Task.FromResult(Result<int>.Failure(NotFound));
    private static Task<Result> Ok() => Task.FromResult(Result.Success());
    private static Task<Result> Failed() => Task.FromResult(Result.Failure(NotFound));

    // ---- same outcomes as a pending source --------------------------------------------------------------------------

    [Fact]
    public async Task Value_steps_produce_the_same_outcomes()
    {
        Assert.Equal(10, (await Five().MapAsync(n => n * 2)).ResultValue);
        Assert.Equal(6, (await Five().BindAsync(n => Result<int>.Success(n + 1))).ResultValue);
        Assert.Same(Invalid, (await Five().BindAsync(_ => Result<int>.Failure(Invalid))).Error);
        Assert.Same(NotFound, (await Missing().MapAsync(n => n * 2)).Error);
        Assert.True((await Five().BindAsync(_ => Result.Success())).IsSuccess);
        Assert.Equal(7, (await Five().BindAsync(n => Task.FromResult(Result<int>.Success(n + 2)))).ResultValue);
        Assert.Equal(8, (await Five().MapAsync(n => Task.FromResult(n + 3))).ResultValue);
        Assert.Equal("5", await Five().MatchAsync(n => n.ToString(), e => e.Code));
        Assert.Equal(NotFound.Code, await Missing().MatchAsync(n => n.ToString(), e => e.Code));
    }

    [Fact]
    public async Task Value_preserving_steps_produce_the_same_outcomes()
    {
        Assert.Same(Invalid, (await Five().EnsureAsync(n => n > 10, Invalid)).Error);
        Assert.Equal(5, (await Five().EnsureAsync(n => n > 1, Invalid)).ResultValue);
        Assert.Same(Invalid, (await Five().EnsureAsync(_ => Result.Failure(Invalid))).Error);
        Assert.Same(Invalid, (await Missing().MapErrorAsync(_ => Invalid)).Error);
        Assert.Same(NotFound, (await Missing().EnsureAsync(_ => false, Invalid)).Error);

        var calls = new List<string>();
        _ = await Five().TapAsync(n => calls.Add($"tap:{n}"));
        _ = await Missing().TapErrorAsync(e => calls.Add($"tapError:{e.Code}"));
        _ = await Missing().TapAsync(_ => calls.Add("must not run"));
        _ = await Five().TapErrorAsync(_ => calls.Add("must not run"));
        await Five().MatchAsync(n => calls.Add($"match:{n}"), _ => calls.Add("must not run"));

        Assert.Equal(["tap:5", $"tapError:{NotFound.Code}", "match:5"], calls);
    }

    [Fact]
    public async Task Steps_on_a_result_without_value_produce_the_same_outcomes()
    {
        Assert.True((await Ok().BindAsync(() => Result.Success())).IsSuccess);
        Assert.Same(Invalid, (await Ok().BindAsync(() => Result.Failure(Invalid))).Error);
        Assert.Equal(3, (await Ok().BindAsync(() => Result<int>.Success(3))).ResultValue);
        Assert.Same(Invalid, (await Ok().EnsureAsync(() => false, Invalid)).Error);
        Assert.Same(Invalid, (await Failed().MapErrorAsync(_ => Invalid)).Error);
        Assert.Equal("ok", await Ok().MatchAsync(() => "ok", e => e.Code));
        Assert.Same(NotFound, (await Failed().BindAsync(() => Result.Success())).Error);
    }

    // ---- no new task when nothing changes ---------------------------------------------------------------------------

    [Fact]
    public void Unchanged_outcomes_return_the_source_task_itself()
    {
        var five = Five();
        var missing = Missing();
        var ok = Ok();

        Assert.Same(five, five.TapAsync(_ => { }));
        Assert.Same(five, five.TapErrorAsync(_ => { }));
        Assert.Same(five, five.EnsureAsync(n => n > 0, Invalid));
        Assert.Same(five, five.EnsureAsync(_ => Result.Success()));
        Assert.Same(five, five.MapErrorAsync(_ => Invalid));
        Assert.Same(missing, missing.TapAsync(_ => { }));
        Assert.Same(missing, missing.EnsureAsync(_ => false, Invalid));
        Assert.Same(ok, ok.BindAsync(() => Result.Success()));
        Assert.Same(ok, ok.TapAsync(() => { }));
    }

    [Fact]
    public void Changed_outcomes_return_a_new_task()
    {
        var five = Five();
        var missing = Missing();

        Assert.NotSame(five, five.EnsureAsync(_ => false, Invalid));
        Assert.NotSame(missing, missing.MapErrorAsync(_ => Invalid));
    }

    [Fact]
    public void Successes_without_value_share_one_completed_task()
    {
        var first = Five().BindAsync(_ => Result.Success());
        var second = Result.Success().MapErrorAsync(e => Task.FromResult(e));

        Assert.True(first.IsCompletedSuccessfully);
        Assert.Same(first, second);
        Assert.Same(first, Five().BindAsync(_ => Result.Success()));
    }

    // ---- exceptions and cancellation still arrive through the task -------------------------------------------------

    [Fact]
    public async Task An_exception_in_a_synchronous_step_faults_the_task_instead_of_throwing()
    {
        var boom = new InvalidOperationException("boom");

        var task = Five().MapAsync((Func<int, int>)(_ => throw boom));      // must not throw here

        Assert.True(task.IsFaulted);
        Assert.Same(boom, await Assert.ThrowsAsync<InvalidOperationException>(() => task));
        Assert.Same(boom, await Assert.ThrowsAsync<InvalidOperationException>(() => Five().TapAsync((Action<int>)(_ => throw boom))));
        Assert.Same(boom, await Assert.ThrowsAsync<InvalidOperationException>(() => Ok().BindAsync((Func<Result>)(() => throw boom))));
        Assert.Same(boom, await Assert.ThrowsAsync<InvalidOperationException>(() => Five().BindAsync((Func<int, Result>)(_ => throw boom))));
    }

    [Fact]
    public void An_OperationCanceledException_in_a_synchronous_step_cancels_the_task()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var task = Five().TapAsync(_ => cancellation.Token.ThrowIfCancellationRequested());

        Assert.True(task.IsCanceled);
    }

    [Fact]
    public async Task An_async_delegate_that_throws_before_its_first_await_faults_the_task()
    {
        var boom = new InvalidOperationException("boom");

        var bind = Result<int>.Success(1).BindAsync<int, int>(_ => throw boom);   // must not throw here
        var match = Result.Success().MatchAsync<int>(() => throw boom, _ => Task.FromResult(0));

        Assert.Same(boom, await Assert.ThrowsAsync<InvalidOperationException>(() => bind));
        Assert.Same(boom, await Assert.ThrowsAsync<InvalidOperationException>(() => match));
        Assert.Same(boom, await Assert.ThrowsAsync<InvalidOperationException>(
            () => Result<int>.Success(1).MatchAsync((Func<int, Task>)(_ => throw boom), _ => Task.CompletedTask)));
        Assert.Same(boom, await Assert.ThrowsAsync<InvalidOperationException>(
            () => Result.Success().MatchAsync((Func<Task>)(() => throw boom), _ => Task.CompletedTask)));
    }

    [Fact]
    public async Task An_async_delegate_that_returns_a_null_task_faults_with_NullReferenceException()
    {
        var task = Result<int>.Success(1).BindAsync<int, int>(_ => null!);

        await Assert.ThrowsAsync<NullReferenceException>(() => task);
        await Assert.ThrowsAsync<NullReferenceException>(() => Result<int>.Success(1).MatchAsync((Func<int, Task>)(_ => null!), _ => Task.CompletedTask));
        await Assert.ThrowsAsync<NullReferenceException>(() => Result.Success().MatchAsync((Func<Task>)(() => null!), _ => Task.CompletedTask));
    }

    [Fact]
    public async Task An_async_delegate_task_is_returned_as_is()
    {
        var delegateTask = Task.FromResult(Result<int>.Success(9));

        Assert.Same(delegateTask, Result<int>.Success(1).BindAsync(_ => delegateTask));
        Assert.Same(delegateTask, Five().BindAsync(_ => delegateTask));
        Assert.Equal(9, (await Five().BindAsync(_ => delegateTask)).ResultValue);
    }
}
