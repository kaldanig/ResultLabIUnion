using static ResultUnion.Tests.Async.EagerAssert;

namespace ResultUnion.Tests;

/// <summary><c>Result.Try</c> / <c>Result.TryAsync</c> for one exception type: that type becomes a failure, every other exception propagates.</summary>
public class ResultTryExceptionTypeTests
{
    private static Func<T> Throws<T>(Exception exception) => () => throw exception;

    private static Action ThrowsAction(Exception exception) => () => throw exception;

    private static int Parse(string text) => int.Parse(text);

    // Try<T, TException>

    [Fact]
    public void Try_MatchingException_IsMapped()
    {
        Result<int> result = Result.Try(() => Parse("abc"), (FormatException ex) => Error.Validation(ex.Message));

        Assert.Equal(ErrorType.Validation, result.Error.Type);
    }

    [Fact]
    public void Try_DerivedException_IsMapped()
    {
        Result<int> result = Result.Try(Throws<int>(new FileNotFoundException("missing")), (IOException ex) => Error.NotFound(ex.Message));

        Assert.Equal("missing", result.Error.Message);
    }

    [Fact]
    public void Try_OtherException_Propagates()
    {
        Assert.Throws<InvalidOperationException>(() =>
            Result.Try(Throws<int>(new InvalidOperationException()), (FormatException ex) => Error.Validation(ex.Message)));
    }

    [Fact]
    public void Try_NoException_ReturnsValue()
    {
        Assert.Equal(42, Result.Try(() => Parse("42"), (FormatException ex) => Error.Validation(ex.Message)).ResultValue);
    }

    [Fact]
    public void Try_Cancellation_IsNeverCaught_EvenForABaseType()
    {
        Assert.Throws<OperationCanceledException>(() =>
            Result.Try(Throws<int>(new OperationCanceledException()), (SystemException ex) => Error.Unexpected(ex.Message)));
        Assert.Throws<TaskCanceledException>(() =>
            Result.Try(ThrowsAction(new TaskCanceledException()), (OperationCanceledException ex) => Error.Unexpected(ex.Message)));
    }

    [Fact]
    public void Try_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => Result.Try((Func<int>)null!, (FormatException ex) => Error.Validation(ex.Message)));
        Assert.Throws<ArgumentNullException>(() => Result.Try(() => 1, (Func<FormatException, Error>)null!));
        Assert.Throws<ArgumentNullException>(() => Result.Try(Throws<int>(new FormatException()), (FormatException _) => null!));
    }

    // Try<TException> (no value)

    [Fact]
    public void TryAction_MatchingOrOtherException()
    {
        Result mapped = Result.Try(ThrowsAction(new IOException("disk")), (IOException ex) => Error.Unavailable(ex.Message));
        Assert.Equal("disk", mapped.Error.Message);

        Assert.True(Result.Try(() => { }, (IOException ex) => Error.Unavailable(ex.Message)).IsSuccess);
        Assert.Throws<ArgumentException>(() => Result.Try(ThrowsAction(new ArgumentException()), (IOException ex) => Error.Unavailable(ex.Message)));
    }

    // Overload resolution: the lambda parameter type decides; untyped or Exception stays on the catch-all overload.

    [Fact]
    public void OverloadResolution_ParameterTypeSelectsTheOverload()
    {
        // Untyped parameter: the catch-all Try<T>(Func<T>, Func<Exception, Error>), which also catches InvalidOperationException.
        Result<int> catchAll = Result.Try(Throws<int>(new InvalidOperationException("x")), ex => Error.Unexpected(ex.Message));
        Assert.True(catchAll.IsFailure);

        // Typed as Exception: also the catch-all (the non-generic parameter is the better match).
        Result<int> typedException = Result.Try(Throws<int>(new InvalidOperationException("x")), (Exception ex) => Error.Unexpected(ex.Message));
        Assert.True(typedException.IsFailure);
    }

    // TryAsync<T, TException>

    [Fact]
    public async Task TryAsync_MatchingExceptionAfterAwait_IsMapped()
    {
        Task<Result<int>> pending = Result.TryAsync(
            async () => { await Task.Yield(); return Parse("abc"); },
            (FormatException ex) => Error.Validation(ex.Message));

        Assert.Equal(ErrorType.Validation, (await pending).Error.Type);
    }

    [Fact]
    public async Task TryAsync_OtherException_PropagatesThroughTheTask()
    {
        var pending = Result.TryAsync(
            async () => { await Task.Yield(); return Throws<int>(new InvalidOperationException("boom"))(); },
            (FormatException ex) => Error.Validation(ex.Message));

        Assert.Equal("boom", (await Assert.ThrowsAsync<InvalidOperationException>(() => pending)).Message);
    }

    [Fact]
    public async Task TryAsync_ExceptionBeforeTheTaskExists_IsMapped()
    {
        Func<Task<int>> throwsSynchronously = () => throw new FormatException("sync");

        var result = await Result.TryAsync(throwsSynchronously, (FormatException ex) => Error.Validation(ex.Message));

        Assert.Equal("sync", result.Error.Message);
    }

    [Fact]
    public async Task TryAsync_Cancellation_IsNeverCaught()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAsync<TaskCanceledException>(() => Result.TryAsync(
            async () => { await Task.Delay(1000, cancellation.Token); return 1; },
            (OperationCanceledException ex) => Error.Unexpected(ex.Message)));
    }

    // TryAsync<TException> (no value)

    [Fact]
    public async Task TryAsyncAction_BlockLambda_PicksTheOverloadWithoutValue()
    {
        Task<Result> pending = Result.TryAsync(
            async () => { await Task.Yield(); throw new IOException("disk"); },
            (IOException ex) => Error.Unavailable(ex.Message));

        Assert.Equal("disk", (await pending).Error.Message);
        Assert.True((await Result.TryAsync(async () => { await Task.Yield(); }, (IOException ex) => Error.Unavailable(ex.Message))).IsSuccess);
    }

    [Fact]
    public async Task TryAsync_TaskOfT_PicksTheOverloadWithValue()
    {
        // A Task<int>-returning call (such as DbContext.SaveChangesAsync) keeps its value.
        Task<Result<int>> pending = Result.TryAsync(() => Task.FromResult(3), (InvalidOperationException ex) => Error.Concurrency(ex.Message));

        Assert.Equal(3, (await pending).ResultValue);
    }

    [Fact]
    public void TryAsync_NullArguments_ThrowWhenCalled()
    {
        ThrowsEagerly(() => _ = Result.TryAsync((Func<Task<int>>)null!, (FormatException ex) => Error.Validation(ex.Message)));
        ThrowsEagerly(() => _ = Result.TryAsync(() => Task.FromResult(1), (Func<FormatException, Error>)null!));
        ThrowsEagerly(() => _ = Result.TryAsync((Func<Task>)null!, (IOException ex) => Error.Unavailable(ex.Message)));
    }
}
