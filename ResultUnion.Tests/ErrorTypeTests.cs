namespace ResultUnion.Tests;

/// <summary><see cref="ErrorType"/> and its retry semantics.</summary>
public class ErrorTypeTests
{
    [Theory]
    [InlineData(ErrorType.Unavailable, true)]
    [InlineData(ErrorType.TooManyRequests, true)]
    [InlineData(ErrorType.Concurrency, true)]
    [InlineData(ErrorType.Unexpected, false)]
    [InlineData(ErrorType.Validation, false)]
    [InlineData(ErrorType.NotFound, false)]
    [InlineData(ErrorType.Unauthorized, false)]
    [InlineData(ErrorType.Forbidden, false)]
    [InlineData(ErrorType.Conflict, false)]
    [InlineData(ErrorType.BusinessRule, false)]
    [InlineData((ErrorType)99, false)]
    public void IsTransient_IsTrueOnlyWhenARetryMaySucceed(ErrorType type, bool expected)
    {
        Assert.Equal(expected, type.IsTransient());
    }

    [Fact]
    public void Error_IsTransient_FollowsItsType()
    {
        Assert.True(Error.TooManyRequests("Slow down.").IsTransient);
        Assert.True(Error.Unavailable("Down.").IsTransient);
        Assert.False(Error.Validation("Bad.").IsTransient);
        Assert.Throws<ArgumentNullException>(() => ((Error)null!).IsTransient);
    }

    [Fact]
    public void EveryErrorType_IsCoveredByTheTransientTheory()
    {
        // Fails when a new ErrorType is added, as a reminder to decide whether it is transient.
        Assert.Equal(10, Enum.GetValues<ErrorType>().Length);
    }

    [Fact]
    public void TooManyRequests_CanCarryTheWait()
    {
        var error = Error.TooManyRequests("Too many password resets.").WithArgument("retryAfterSeconds", 30);

        Assert.Equal("rate_limit.exceeded", error.Code);
        Assert.Equal(30, error.Arguments["retryAfterSeconds"]);
    }

    // A typical consumer: retry a transient failure a bounded number of times.
    [Fact]
    public void RetryLoop_RetriesTransientFailuresOnly()
    {
        static Result<int> WithRetry(Func<Result<int>> operation, int attempts)
        {
            var result = operation();
            for (var attempt = 1; attempt < attempts && result.TryGetError(out var error) && error.IsTransient; attempt++)
            {
                result = operation();
            }

            return result;
        }

        var calls = 0;
        Assert.Equal(3, WithRetry(() => ++calls < 3 ? Error.Unavailable("Down.") : calls, attempts: 5).ResultValue);

        calls = 0;
        Assert.True(WithRetry(() => { calls++; return Error.Validation("Bad."); }, attempts: 5).IsFailure);
        Assert.Equal(1, calls);
    }
}
