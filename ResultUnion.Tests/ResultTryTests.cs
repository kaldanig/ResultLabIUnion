using static ResultUnion.Tests.Async.EagerAssert;

namespace ResultUnion.Tests;

/// <summary><c>Result.Try</c> and <c>Result.TryAsync</c>: turning exceptions from throwing code into failures.</summary>
public class ResultTryTests
{
    private static Error ToError(Exception exception) => Error.Unexpected($"{exception.GetType().Name}: {exception.Message}");

    private static Func<Exception, Error> MustNotMap => _ => throw new InvalidOperationException("onException must not run.");

    // A lambda that only throws has no return type; typed delegates make it explicit which overload is under test.
    private static Func<T> Throws<T>(Exception exception) => () => throw exception;

    private static Action ThrowsAction(Exception exception) => () => throw exception;

    private static Func<T> Returns<T>(T value) => () => value;

    // Try<T>

    [Fact]
    public void Try_NoException_ReturnsValue()
    {
        Assert.Equal(42, Result.Try(() => int.Parse("42"), ToError).ResultValue);
    }

    [Fact]
    public void Try_Exception_IsMappedToFailure()
    {
        Exception? received = null;
        var thrown = new FormatException("bad");

        var result = Result.Try(Throws<int>(thrown), ex => { received = ex; return Error.Validation(ex.Message); });

        Assert.Equal("bad", result.Error.Message);
        Assert.Same(thrown, received);
    }

    [Fact]
    public void Try_Cancellation_IsNotCaught()
    {
        Assert.Throws<OperationCanceledException>(() => Result.Try(Throws<int>(new OperationCanceledException()), MustNotMap));
        Assert.Throws<TaskCanceledException>(() => Result.Try(Throws<int>(new TaskCanceledException()), MustNotMap));
    }

    [Fact]
    public void Try_ActionReturnsNull_ThrowsInsteadOfBeingMapped()
    {
        Assert.Throws<ArgumentNullException>(() => Result.Try(Returns<string>(null!), MustNotMap));
    }

    [Fact]
    public void Try_OnExceptionThrowsOrReturnsNull_Propagates()
    {
        Assert.Throws<InvalidOperationException>(() => Result.Try(Throws<int>(new FormatException()), MustNotMap));
        Assert.Throws<ArgumentNullException>(() => Result.Try(Throws<int>(new FormatException()), _ => null!));
    }

    [Fact]
    public void Try_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => Result.Try((Func<int>)null!, ToError));
        Assert.Throws<ArgumentNullException>(() => Result.Try(() => 1, null!));
    }

    // Try (no value)

    [Fact]
    public void TryAction_SuccessOrMappedFailure()
    {
        var calls = 0;

        Assert.True(Result.Try(() => { calls++; }, ToError).IsSuccess);
        Assert.Equal("Unexpected", Result.Try(ThrowsAction(new IOException("disk")), ToError).Error.Type.ToString());
        Assert.Equal(1, calls);
    }

    [Fact]
    public void TryAction_CancellationAndNullArguments_Throw()
    {
        Assert.Throws<OperationCanceledException>(() => Result.Try(ThrowsAction(new OperationCanceledException()), MustNotMap));
        Assert.Throws<ArgumentNullException>(() => Result.Try((Action)null!, ToError));
        Assert.Throws<ArgumentNullException>(() => Result.Try(() => { }, null!));
    }

    // TryAsync<T>

    [Fact]
    public async Task TryAsync_NoException_ReturnsValue()
    {
        var result = await Result.TryAsync(async () => { await Task.Yield(); return 42; }, ToError);

        Assert.Equal(42, result.ResultValue);
    }

    [Fact]
    public async Task TryAsync_ExceptionAfterAwait_IsMapped()
    {
        var result = await Result.TryAsync<int>(async () => { await Task.Yield(); throw new HttpRequestException("down"); }, ToError);

        Assert.Equal("HttpRequestException: down", result.Error.Message);
    }

    [Fact]
    public async Task TryAsync_ExceptionBeforeTheTaskExists_IsMapped()
    {
        Func<Task<int>> throwsSynchronously = () => throw new InvalidDataException("sync");

        var result = await Result.TryAsync(throwsSynchronously, ToError);

        Assert.Equal("InvalidDataException: sync", result.Error.Message);
    }

    [Fact]
    public async Task TryAsync_Cancellation_IsNotCaught()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAsync<TaskCanceledException>(() =>
            Result.TryAsync(async () => { await Task.Delay(1000, cancellation.Token); return 1; }, MustNotMap));
    }

    [Fact]
    public async Task TryAsync_ValueIsNull_ThrowsThroughTask()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => Result.TryAsync(() => Task.FromResult<string>(null!), MustNotMap));
    }

    [Fact]
    public void TryAsync_NullArguments_ThrowWhenCalled()
    {
        ThrowsEagerly(() => _ = Result.TryAsync((Func<Task<int>>)null!, ToError));
        ThrowsEagerly(() => _ = Result.TryAsync(() => Task.FromResult(1), null!));
        ThrowsEagerly(() => _ = Result.TryAsync((Func<Task>)null!, ToError));
        ThrowsEagerly(() => _ = Result.TryAsync(() => Task.CompletedTask, null!));
    }

    // TryAsync (no value)

    [Fact]
    public async Task TryAsyncAction_SuccessOrMappedFailure()
    {
        Assert.True((await Result.TryAsync(async () => await Task.Yield(), ToError)).IsSuccess);
        Assert.Equal("IOException: disk", (await Result.TryAsync(async () => { await Task.Yield(); throw new IOException("disk"); }, ToError)).Error.Message);
    }

    // Composition: Try is the entry point, the rest of the pipeline continues as usual.

    [Theory]
    [InlineData("21", "ok:42")]
    [InlineData("abc", "error:validation.failed")]
    [InlineData("-1", "error:business_rule.violated")]
    public async Task TryAsync_StartsAPipeline(string input, string expected)
    {
        var outcome = await Result.TryAsync(async () => { await Task.Yield(); return int.Parse(input); }, ex => Error.Validation(ex.Message))
            .EnsureAsync(n => n > 0, Error.BusinessRule("Must be positive."))
            .MapAsync(n => n * 2)
            .MatchAsync(n => $"ok:{n}", e => $"error:{e.Code}");

        Assert.Equal(expected, outcome);
    }
}
