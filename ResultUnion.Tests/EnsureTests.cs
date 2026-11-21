namespace ResultUnion.Tests;

/// <summary><c>Ensure</c> on <see cref="Result{T}"/> and <see cref="Result"/>.</summary>
public class EnsureTests
{
    private static readonly Error Negative = Error.Validation("Number must be positive.");
    private static readonly Error NotFound = Error.NotFound("Item was not found.");

    // Result<T>

    [Fact]
    public void ResultOfT_PredicateSatisfied_ReturnsSameSuccess()
    {
        var result = Result<int>.Success(5);

        Assert.Equal(result, result.Ensure(n => n > 0, Negative));
        Assert.Equal(result, result.Ensure(n => n > 0, n => Error.Validation($"{n} is not positive.")));
    }

    [Fact]
    public void ResultOfT_PredicateNotSatisfied_ReturnsGivenError()
    {
        Assert.Same(Negative, Result<int>.Success(-1).Ensure(n => n > 0, Negative).Error);
    }

    [Fact]
    public void ResultOfT_PredicateNotSatisfied_BuildsErrorFromValue()
    {
        var error = Result<int>.Success(-1).Ensure(n => n > 0, n => Error.Validation($"{n} is not positive.")).Error;

        Assert.Equal("-1 is not positive.", error.Message);
    }

    [Fact]
    public void ResultOfT_Failure_PropagatesErrorWithoutInvokingDelegates()
    {
        var calls = 0;
        var failure = Result<int>.Failure(NotFound);

        Assert.Same(NotFound, failure.Ensure(_ => { calls++; return false; }, Negative).Error);
        Assert.Same(NotFound, failure.Ensure(_ => { calls++; return false; }, _ => { calls++; return Negative; }).Error);
        Assert.Equal(0, calls);
    }

    [Fact]
    public void ResultOfT_Default_PropagatesDefaultResultError()
    {
        Assert.Same(Error.DefaultResult, default(Result<int>).Ensure(_ => false, Negative).Error);
    }

    [Fact]
    public void ResultOfT_SatisfiedPredicate_DoesNotInvokeErrorFactory()
    {
        var called = false;

        _ = Result<int>.Success(1).Ensure(_ => true, _ => { called = true; return Negative; });

        Assert.False(called);
    }

    [Fact]
    public void ResultOfT_NullArguments_Throw()
    {
        var result = Result<int>.Success(1);

        Assert.Throws<ArgumentNullException>(() => result.Ensure(null!, Negative));
        Assert.Throws<ArgumentNullException>(() => result.Ensure(_ => true, (Error)null!));
        Assert.Throws<ArgumentNullException>(() => result.Ensure(_ => true, (Func<int, Error>)null!));
        Assert.Throws<ArgumentNullException>(() => result.Ensure(_ => false, _ => null!));
    }

    [Theory]
    [InlineData("12", "ok:12")]
    [InlineData("-3", "error:validation.failed")]
    [InlineData("7", "error:business_rule.violated")]
    public void ResultOfT_ChainedEnsures_ShortCircuitOnFirstFailure(string input, string expected)
    {
        var outcome = Result<string>.Success(input)
            .Map(int.Parse)
            .Ensure(n => n > 0, Negative)
            .Ensure(n => n % 2 == 0, n => Error.BusinessRule($"{n} is odd."))
            .Match(n => $"ok:{n}", e => $"error:{e.Code}");

        Assert.Equal(expected, outcome);
    }

    // Result<T>.Ensure(Func<T, Result>)

    private static Result EnsureEven(int number) =>
        number % 2 == 0
            ? Result.Success()
            : Error.BusinessRule($"{number} is odd.");

    [Fact]
    public void ResultOfT_CheckSucceeds_KeepsValue()
    {
        var result = Result<int>.Success(4);

        Assert.Equal(result, result.Ensure(EnsureEven));
    }

    [Fact]
    public void ResultOfT_CheckFails_ReturnsCheckError()
    {
        var error = Result<int>.Success(3).Ensure(EnsureEven).Error;

        Assert.Equal(ErrorType.BusinessRule, error.Type);
        Assert.Equal("3 is odd.", error.Message);
    }

    [Fact]
    public void ResultOfT_CheckReturnsDefaultResult_ReturnsDefaultResultError()
    {
        Assert.Same(Error.DefaultResult, Result<int>.Success(1).Ensure(_ => default).Error);
    }

    [Fact]
    public void ResultOfT_Failure_PropagatesErrorWithoutInvokingCheck()
    {
        var called = false;

        var result = Result<int>.Failure(NotFound).Ensure(n => { called = true; return EnsureEven(n); });

        Assert.Same(NotFound, result.Error);
        Assert.False(called);
    }

    [Fact]
    public void ResultOfT_NullCheck_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => Result<int>.Success(1).Ensure((Func<int, Result>)null!));
    }

    [Theory]
    [InlineData("12", "ok:12")]
    [InlineData("-4", "error:validation.failed")]
    [InlineData("7", "error:business_rule.violated")]
    public void ResultOfT_EnsureWithValidator_ComposesWithPredicateEnsure(string input, string expected)
    {
        var outcome = Result<string>.Success(input)
            .Map(int.Parse)
            .Ensure(n => n > 0, Negative)
            .Ensure(EnsureEven)
            .Match(n => $"ok:{n}", e => $"error:{e.Code}");

        Assert.Equal(expected, outcome);
    }

    // Result

    [Fact]
    public void Result_PredicateSatisfied_ReturnsSuccess()
    {
        Assert.True(Result.Success().Ensure(() => true, Negative).IsSuccess);
        Assert.True(Result.Success().Ensure(() => true, () => Negative).IsSuccess);
    }

    [Fact]
    public void Result_PredicateNotSatisfied_ReturnsError()
    {
        Assert.Same(Negative, Result.Success().Ensure(() => false, Negative).Error);
        Assert.Same(Negative, Result.Success().Ensure(() => false, () => Negative).Error);
    }

    [Fact]
    public void Result_Failure_PropagatesErrorWithoutInvokingDelegates()
    {
        var calls = 0;
        var failure = Result.Failure(NotFound);

        Assert.Same(NotFound, failure.Ensure(() => { calls++; return false; }, Negative).Error);
        Assert.Same(NotFound, failure.Ensure(() => { calls++; return false; }, () => { calls++; return Negative; }).Error);
        Assert.Equal(0, calls);
    }

    [Fact]
    public void Result_Default_PropagatesDefaultResultError()
    {
        Assert.Same(Error.DefaultResult, default(Result).Ensure(() => false, Negative).Error);
    }

    [Fact]
    public void Result_NullArguments_Throw()
    {
        var result = Result.Success();

        Assert.Throws<ArgumentNullException>(() => result.Ensure(null!, Negative));
        Assert.Throws<ArgumentNullException>(() => result.Ensure(() => true, (Error)null!));
        Assert.Throws<ArgumentNullException>(() => result.Ensure(() => true, (Func<Error>)null!));
        Assert.Throws<ArgumentNullException>(() => result.Ensure(() => false, () => null!));
    }
}
