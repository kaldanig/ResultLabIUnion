namespace ResultUnion.Tests;

public class ResultOfTTests
{
    private static readonly Error NotFound = Error.NotFound("Item was not found.");

    [Fact]
    public void Success_ExposesValue()
    {
        var result = Result<int>.Success(5);

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Equal(5, result.ResultValue);
        Assert.Throws<InvalidOperationException>(() => result.Error);
    }

    [Fact]
    public void Failure_ExposesError()
    {
        var result = Result<int>.Failure(NotFound);

        Assert.False(result.IsSuccess);
        Assert.True(result.IsFailure);
        Assert.Same(NotFound, result.Error);
        Assert.Throws<InvalidOperationException>(() => result.ResultValue);
    }

    // A shared exception instance would share its stack trace and Data between throws, across threads too.
    [Fact]
    public void ResultValue_OnFailure_ThrowsANewExceptionEachTime()
    {
        var result = Result<int>.Failure(NotFound);

        var first = Assert.Throws<InvalidOperationException>(() => result.ResultValue);
        var second = Assert.Throws<InvalidOperationException>(() => result.ResultValue);

        Assert.NotSame(first, second);
        Assert.Equal("Cannot access 'ResultValue' when the 'Result' is a failure.", first.Message);
    }

    [Fact]
    public void Error_OnSuccess_ThrowsANewExceptionEachTime()
    {
        var result = Result<int>.Success(5);

        var first = Assert.Throws<InvalidOperationException>(() => result.Error);
        var second = Assert.Throws<InvalidOperationException>(() => result.Error);

        Assert.NotSame(first, second);
        Assert.Equal("Cannot access 'Error' when the 'Result' is a success.", first.Message);
    }

    [Fact]
    public void Default_IsFailureWithDefaultResultError()
    {
        var result = default(Result<int>);

        Assert.True(result.IsFailure);
        Assert.Same(Error.DefaultResult, result.Error);
    }

    [Fact]
    public void Constructor_NullValue_Throws()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new Result<string>((string)null!));
        Assert.Equal("value", exception.ParamName);
    }

    [Fact]
    public void Constructor_NullError_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new Result<string>((Error)null!));
    }

    [Fact]
    public void ImplicitConversions_CreateMatchingState()
    {
        Result<string> success = "ok";
        Result<string> failure = NotFound;

        Assert.True(success.IsSuccess);
        Assert.Equal("ok", success.ResultValue);
        Assert.True(failure.IsFailure);
        Assert.Same(NotFound, failure.Error);
    }

    [Fact]
    public void FactoryHelpersOnResult_CreateGenericResults()
    {
        Assert.Equal(3, Result.Success(3).ResultValue);
        Assert.Same(NotFound, Result.Failure<int>(NotFound).Error);
    }

    [Fact]
    public void Match_InvokesBranchMatchingState()
    {
        Assert.Equal("value:2", Result<int>.Success(2).Match(v => $"value:{v}", e => $"error:{e.Code}"));
        Assert.Equal("error:resource.not_found", Result<int>.Failure(NotFound).Match(v => $"value:{v}", e => $"error:{e.Code}"));
    }

    [Fact]
    public void Match_NullDelegate_Throws()
    {
        var result = Result<int>.Success(1);

        Assert.Throws<ArgumentNullException>(() => result.Match(null!, e => 0));
        Assert.Throws<ArgumentNullException>(() => result.Match(v => v, null!));
    }

    [Fact]
    public void MatchAction_Success_InvokesOnlyOnSuccessWithValue()
    {
        var calls = new List<string>();

        Result<int>.Success(7).Match(v => calls.Add($"success:{v}"), e => calls.Add($"failure:{e.Code}"));

        Assert.Equal(["success:7"], calls);
    }

    [Fact]
    public void MatchAction_Failure_InvokesOnlyOnFailureWithError()
    {
        var calls = new List<string>();

        Result<int>.Failure(NotFound).Match(v => calls.Add($"success:{v}"), e => calls.Add($"failure:{e.Code}"));

        Assert.Equal(["failure:resource.not_found"], calls);
    }

    [Fact]
    public void MatchAction_Default_InvokesOnFailureWithDefaultResultError()
    {
        Error? received = null;

        default(Result<int>).Match(_ => Assert.Fail("onSuccess must not run."), e => received = e);

        Assert.Same(Error.DefaultResult, received);
    }

    [Fact]
    public void MatchAction_NullDelegate_Throws()
    {
        var result = Result<int>.Success(1);

        Assert.Throws<ArgumentNullException>(() => result.Match(null!, (Action<Error>)(_ => { })));
        Assert.Throws<ArgumentNullException>(() => result.Match((Action<int>)(_ => { }), null!));
    }

    // TryGetResultValue: 'out var' must compile, unlike the union's two TryGetValue overloads.

    [Fact]
    public void TryGetResultValue_Success_ReturnsValue()
    {
        Assert.True(Result<int>.Success(7).TryGetResultValue(out var value));
        Assert.Equal(7, value);
    }

    [Fact]
    public void TryGetResultValue_Failure_ReturnsFalse()
    {
        Assert.False(Result<int>.Failure(NotFound).TryGetResultValue(out var value));
        Assert.Equal(0, value);
    }

    [Fact]
    public void TryGetResultValue_Default_ReturnsFalse()
    {
        Assert.False(default(Result<string>).TryGetResultValue(out var value));
        Assert.Null(value);
    }

    [Fact]
    public void TryGetResultValue_AndTryGetError_CoverBothTracks()
    {
        static string Describe(Result<string> result) =>
            result.TryGetResultValue(out var value) ? $"value:{value}"
            : result.TryGetError(out var error) ? $"error:{error.Code}"
            : "unreachable";

        Assert.Equal("value:hi", Describe("hi"));
        Assert.Equal("error:resource.not_found", Describe(NotFound));
    }

    [Fact]
    public void GetValueOrDefault_ReturnsValueOrFallback()
    {
        Assert.Equal(4, Result<int>.Success(4).GetValueOrDefault());
        Assert.Equal(0, Result<int>.Failure(NotFound).GetValueOrDefault());
        Assert.Equal(4, Result<int>.Success(4).GetValueOrDefault(9));
        Assert.Equal(9, Result<int>.Failure(NotFound).GetValueOrDefault(9));
        Assert.Null(Result<string>.Failure(NotFound).GetValueOrDefault());
    }

    [Fact]
    public void ToResult_KeepsStateAndError()
    {
        Assert.True(Result<int>.Success(1).ToResult().IsSuccess);
        Assert.Same(NotFound, Result<int>.Failure(NotFound).ToResult().Error);
    }

    [Fact]
    public void ToString_DescribesState()
    {
        Assert.Equal("Success(42)", Result<int>.Success(42).ToString());
        Assert.Equal("Failure(resource.not_found: Item was not found.)", Result<int>.Failure(NotFound).ToString());
    }

    [Fact]
    public void Equality_ComparesStateAndContent()
    {
        Assert.Equal(Result<int>.Success(1), Result<int>.Success(1));
        Assert.NotEqual(Result<int>.Success(1), Result<int>.Success(2));
        Assert.Equal(Result<int>.Failure(Error.NotFound("x")), Result<int>.Failure(Error.NotFound("x")));
        Assert.NotEqual(Result<int>.Success(0), Result<int>.Failure(Error.NotFound("x")));
    }
}
