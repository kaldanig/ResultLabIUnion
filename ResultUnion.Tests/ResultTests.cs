namespace ResultUnion.Tests;

public class ResultTests
{
    private static readonly Error First = Error.Validation("First failed.");
    private static readonly Error Second = Error.Conflict("Second failed.");

    [Fact]
    public void Success_HasNoError()
    {
        var result = Result.Success();

        Assert.True(result.IsSuccess);
        Assert.Throws<InvalidOperationException>(() => result.Error);
        Assert.False(result.TryGetError(out var error));
        Assert.Null(error);
    }

    [Fact]
    public void Failure_ExposesError()
    {
        Result result = First;

        Assert.True(result.IsFailure);
        Assert.Same(First, result.Error);
        Assert.True(result.TryGetError(out var error));
        Assert.Same(First, error);
    }

    [Fact]
    public void Default_IsFailureWithDefaultResultError()
    {
        var result = default(Result);

        Assert.True(result.IsFailure);
        Assert.Same(Error.DefaultResult, result.Error);
    }

    [Fact]
    public void Failure_NullError_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => Result.Failure(null!));
    }

    [Fact]
    public void CombineSpan_ReturnsFirstFailureInOrder()
    {
        Assert.True(Result.Combine().IsSuccess);
        Assert.True(Result.Combine(Result.Success(), Result.Success()).IsSuccess);
        Assert.Same(First, Result.Combine(Result.Success(), First, Second).Error);
    }

    [Fact]
    public void CombineEnumerable_AcceptsGenericResults()
    {
        IResultState[] results = [Result.Success(), Result<int>.Success(1), Result<int>.Failure(Second), Result.Failure(First)];

        Assert.Same(Second, Result.Combine(results).Error);
        Assert.True(Result.Combine([Result.Success(), Result<int>.Success(1)]).IsSuccess);
    }

    [Fact]
    public void CombineEnumerable_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => Result.Combine((IEnumerable<IResultState>)null!));
        Assert.Throws<ArgumentNullException>(() => Result.Combine(new IResultState[] { null! }));
    }

    [Fact]
    public void Match_InvokesBranchMatchingState()
    {
        Assert.Equal("ok", Result.Success().Match(() => "ok", e => e.Code));
        Assert.Equal("validation.failed", Result.Failure(First).Match(() => "ok", e => e.Code));

        var calls = new List<string>();
        Result.Success().Match(() => calls.Add("success"), e => calls.Add("failure"));
        Result.Failure(First).Match(() => calls.Add("success"), e => calls.Add("failure"));
        Assert.Equal(["success", "failure"], calls);
    }

    [Fact]
    public void Bind_RunsNextOnlyOnSuccess()
    {
        var called = false;

        Assert.Same(Second, Result.Success().Bind(() => Second).Error);
        Assert.Same(First, Result.Failure(First).Bind(() => { called = true; return Result.Success(); }).Error);
        Assert.False(called);
    }

    [Fact]
    public void BindOfT_RunsNextOnlyOnSuccess()
    {
        Assert.Equal(10, Result.Success().Bind(() => Result.Success(10)).ResultValue);
        Assert.Same(First, Result.Failure(First).Bind(() => Result.Success(10)).Error);
    }

    [Fact]
    public void MapError_TransformsOnlyFailures()
    {
        Assert.True(Result.Success().MapError(_ => Second).IsSuccess);
        Assert.Same(Second, Result.Failure(First).MapError(_ => Second).Error);
    }

    [Fact]
    public void Tap_And_TapError_RunOnlyForMatchingState()
    {
        var calls = new List<string>();

        _ = Result.Success().Tap(() => calls.Add("tap")).TapError(_ => calls.Add("tapError"));
        _ = Result.Failure(First).Tap(() => calls.Add("tap")).TapError(e => calls.Add($"tapError:{e.Code}"));

        Assert.Equal(["tap", "tapError:validation.failed"], calls);
    }

    [Fact]
    public void ToString_DescribesState()
    {
        Assert.Equal("Success", Result.Success().ToString());
        Assert.Equal("Failure(validation.failed: First failed.)", Result.Failure(First).ToString());
    }
}
