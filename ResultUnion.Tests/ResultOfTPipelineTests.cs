namespace ResultUnion.Tests;

/// <summary>Railway-style operations on <see cref="Result{T}"/>: <c>Map</c>, <c>Bind</c>, <c>MapError</c>, <c>Tap</c>, <c>TapError</c>, <c>TryGetError</c>.</summary>
public class ResultOfTPipelineTests
{
    private static readonly Error NotFound = Error.NotFound("Item was not found.");
    private static readonly Error Invalid = Error.Validation("Value is invalid.");

    private static Result<int> ParsePositive(string text) =>
        int.TryParse(text, out var number) && number > 0
            ? number
            : Invalid;

    private static Result EnsureEven(int number) =>
        number % 2 == 0
            ? Result.Success()
            : Error.BusinessRule("Number must be even.");

    // TryGetError

    [Fact]
    public void TryGetError_Success_ReturnsFalse()
    {
        Assert.False(Result<int>.Success(1).TryGetError(out var error));
        Assert.Null(error);
    }

    [Fact]
    public void TryGetError_Failure_ReturnsError()
    {
        Assert.True(Result<int>.Failure(NotFound).TryGetError(out var error));
        Assert.Same(NotFound, error);
    }

    [Fact]
    public void TryGetError_Default_ReturnsDefaultResultError()
    {
        Assert.True(default(Result<int>).TryGetError(out var error));
        Assert.Same(Error.DefaultResult, error);
    }

    // Map

    [Fact]
    public void Map_Success_TransformsValue()
    {
        var result = Result<int>.Success(21).Map(n => $"#{n * 2}");

        Assert.Equal("#42", result.ResultValue);
    }

    [Fact]
    public void Map_Failure_PropagatesErrorWithoutCallingMapper()
    {
        var called = false;

        var result = Result<int>.Failure(NotFound).Map(n => { called = true; return n.ToString(); });

        Assert.Same(NotFound, result.Error);
        Assert.False(called);
    }

    [Fact]
    public void Map_MapperReturnsNull_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => Result<int>.Success(1).Map<string>(_ => null!));
    }

    // Bind

    [Fact]
    public void Bind_Success_ChainsValueProducingOperation()
    {
        Assert.Equal(8, Result<string>.Success("8").Bind(ParsePositive).ResultValue);
        Assert.Same(Invalid, Result<string>.Success("-1").Bind(ParsePositive).Error);
    }

    [Fact]
    public void Bind_Success_ChainsNonGenericOperation()
    {
        Assert.True(Result<int>.Success(4).Bind(EnsureEven).IsSuccess);
        Assert.Equal(ErrorType.BusinessRule, Result<int>.Success(3).Bind(EnsureEven).Error.Type);
    }

    [Fact]
    public void Bind_Failure_PropagatesErrorWithoutCallingNext()
    {
        var called = false;

        Result<int> generic = Result<string>.Failure(NotFound).Bind(s => { called = true; return ParsePositive(s); });
        Result nonGeneric = Result<int>.Failure(NotFound).Bind(n => { called = true; return EnsureEven(n); });

        Assert.Same(NotFound, generic.Error);
        Assert.Same(NotFound, nonGeneric.Error);
        Assert.False(called);
    }

    [Fact]
    public void Bind_Default_PropagatesDefaultResultError()
    {
        Assert.Same(Error.DefaultResult, default(Result<string>).Bind(ParsePositive).Error);
    }

    // MapError

    [Fact]
    public void MapError_TransformsOnlyFailures()
    {
        Assert.Equal(1, Result<int>.Success(1).MapError(_ => Invalid).ResultValue);
        Assert.Same(Invalid, Result<int>.Failure(NotFound).MapError(_ => Invalid).Error);
    }

    // Tap / TapError

    [Fact]
    public void Tap_And_TapError_RunOnlyForMatchingStateAndReturnSameResult()
    {
        var calls = new List<string>();
        var success = Result<int>.Success(5);
        var failure = Result<int>.Failure(NotFound);

        Assert.Equal(success, success.Tap(n => calls.Add($"tap:{n}")).TapError(_ => calls.Add("tapError")));
        Assert.Equal(failure, failure.Tap(n => calls.Add($"tap:{n}")).TapError(e => calls.Add($"tapError:{e.Code}")));

        Assert.Equal(["tap:5", "tapError:resource.not_found"], calls);
    }

    // Null delegates

    [Fact]
    public void Operations_NullDelegate_Throw()
    {
        var result = Result<int>.Success(1);

        Assert.Throws<ArgumentNullException>(() => result.Map<int>(null!));
        Assert.Throws<ArgumentNullException>(() => result.Bind((Func<int, Result<int>>)null!));
        Assert.Throws<ArgumentNullException>(() => result.Bind((Func<int, Result>)null!));
        Assert.Throws<ArgumentNullException>(() => result.MapError(null!));
        Assert.Throws<ArgumentNullException>(() => result.Tap(null!));
        Assert.Throws<ArgumentNullException>(() => result.TapError(null!));
    }

    // End-to-end pipeline

    [Theory]
    [InlineData("10", "ok:20")]
    [InlineData("7", "error:business_rule.violated")]
    [InlineData("abc", "error:validation.failed")]
    public void Pipeline_ShortCircuitsOnFirstFailure(string input, string expected)
    {
        var outcome = Result<string>.Success(input)
            .Bind(ParsePositive)
            .Bind(n => EnsureEven(n).Bind(() => Result.Success(n)))
            .Map(n => n * 2)
            .Match(n => $"ok:{n}", e => $"error:{e.Code}");

        Assert.Equal(expected, outcome);
    }
}
