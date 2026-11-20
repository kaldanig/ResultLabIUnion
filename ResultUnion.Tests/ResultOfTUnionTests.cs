namespace ResultUnion.Tests;

/// <summary>Union behaviour of <see cref="Result{T}"/>: pattern matching, <c>Value</c>, <c>HasValue</c> and <c>TryGetValue</c>.</summary>
public class ResultOfTUnionTests
{
    private static readonly Error NotFound = Error.NotFound("Item was not found.");

    private static string Describe<T>(Result<T> result)
        where T : notnull
        => result switch
        {
            T value => $"value:{value}",
            Error error => $"error:{error.Code}",
        };

    // Regression: for a value-type T the backing field is never null, so a failure used to match as 'default(T)'.

    [Fact]
    public void Switch_FailedResultOfValueType_MatchesErrorCase()
    {
        Result<int> result = NotFound;

        Assert.Equal("error:resource.not_found", Describe(result));
    }

    [Fact]
    public void Switch_DefaultResultOfValueType_MatchesErrorCase()
    {
        Assert.Equal("error:result.default", Describe(default(Result<int>)));
    }

    [Fact]
    public void Is_FailedResultOfValueType_DoesNotMatchValueCase()
    {
        Result<int> result = NotFound;

        Assert.False(result is int);
        Assert.True(result is Error { Type: ErrorType.NotFound });
    }

    [Fact]
    public void Value_FailedResultOfValueType_ReturnsError()
    {
        Result<int> result = NotFound;

        Assert.Same(NotFound, result.Value);
    }

    [Fact]
    public void Value_DefaultResultOfValueType_ReturnsDefaultResultError()
    {
        Assert.Same(Error.DefaultResult, default(Result<int>).Value);
    }

    // Success cases, including a value equal to default(T).

    [Theory]
    [InlineData(42)]
    [InlineData(0)]
    public void Switch_SuccessfulResultOfValueType_MatchesValueCase(int value)
    {
        Assert.Equal($"value:{value}", Describe<int>(value));
    }

    [Fact]
    public void Value_SuccessfulResultOfValueType_ReturnsBoxedValue()
    {
        Result<int> result = 0;

        Assert.Equal(0, Assert.IsType<int>(result.Value));
    }

    [Fact]
    public void Switch_ResultOfReferenceType_MatchesEachCase()
    {
        Assert.Equal("value:hello", Describe<string>("hello"));
        Assert.Equal("error:resource.not_found", Describe<string>(NotFound));
        Assert.Equal("error:result.default", Describe(default(Result<string>)));
    }

    // HasValue / TryGetValue.

    [Fact]
    public void HasValue_IsAlwaysTrue()
    {
        Assert.True(Result<int>.Success(1).HasValue);
        Assert.True(Result<int>.Failure(NotFound).HasValue);
        Assert.True(default(Result<int>).HasValue);
    }

    [Fact]
    public void TryGetValue_Success_ReturnsValueAndNoError()
    {
        Result<int> result = 7;

        Assert.True(result.TryGetValue(out int value));
        Assert.Equal(7, value);

        Assert.False(result.TryGetValue(out Error? error));
        Assert.Null(error);
    }

    [Fact]
    public void TryGetValue_Failure_ReturnsErrorAndNoValue()
    {
        Result<int> result = NotFound;

        Assert.False(result.TryGetValue(out int value));
        Assert.Equal(0, value);

        Assert.True(result.TryGetValue(out Error? error));
        Assert.Same(NotFound, error);
    }

    [Fact]
    public void TryGetValue_Default_ReturnsDefaultResultError()
    {
        var result = default(Result<string>);

        Assert.False(result.TryGetValue(out string? value));
        Assert.Null(value);

        Assert.True(result.TryGetValue(out Error? error));
        Assert.Same(Error.DefaultResult, error);
    }
}
