namespace ResultUnion.Tests;

/// <summary><c>Result.CombineAll</c>: reports every failure, not just the first.</summary>
public class CombineAllTests
{
    private static readonly Error EmailRequired = Error.Validation("Email is required.").WithField("email");
    private static readonly Error AgeInvalid = Error.Validation("'abc' is not a number.").WithField("age");
    private static readonly Error Forbidden = Error.Forbidden("No access.");

    // Span overload

    [Fact]
    public void Span_AllSucceed_ReturnsSuccess()
    {
        Assert.True(Result.CombineAll().IsSuccess);
        Assert.True(Result.CombineAll(Result.Success(), Result.Success()).IsSuccess);
    }

    [Fact]
    public void Span_SingleFailure_IsReturnedUnchanged()
    {
        Assert.Same(Forbidden, Result.CombineAll(Result.Success(), Forbidden, Result.Success()).Error);
    }

    [Fact]
    public void Span_SeveralFailures_AreGroupedInOrder()
    {
        var error = Result.CombineAll(EmailRequired, Result.Success(), AgeInvalid).Error;

        Assert.Equal("validation.failed", error.Code);
        Assert.Equal([EmailRequired, AgeInvalid], error.Details);
    }

    [Fact]
    public void Span_DefaultResult_CountsAsFailure()
    {
        Assert.Same(Error.DefaultResult, Result.CombineAll(Result.Success(), default).Error);
    }

    // Enumerable overload: mixes Result<T> of different types

    [Fact]
    public void Enumerable_MixedTypes_GroupsEveryFailure()
    {
        IAnyResult[] results =
        [
            Result<string>.Failure(EmailRequired),
            Result<int>.Success(30),
            Result<int>.Failure(AgeInvalid),
            Result.Failure(Forbidden),
        ];

        var error = Result.CombineAll(results).Error;

        Assert.Equal([EmailRequired, AgeInvalid, Forbidden], error.Details);
        Assert.Equal(ErrorType.Validation, error.Type);
    }

    [Fact]
    public void Enumerable_SingleFailureOrNone()
    {
        Assert.True(Result.CombineAll([Result<int>.Success(1), Result.Success()]).IsSuccess);
        Assert.Same(AgeInvalid, Result.CombineAll([Result<int>.Success(1), Result<int>.Failure(AgeInvalid)]).Error);
    }

    [Fact]
    public void Enumerable_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => Result.CombineAll((IEnumerable<IAnyResult>)null!));
        Assert.Throws<ArgumentNullException>(() => Result.CombineAll(new IAnyResult[] { Result.Success(), null! }));
    }

    [Fact]
    public void Combine_StillStopsAtTheFirstFailure()
    {
        Assert.Same(EmailRequired, Result.Combine(EmailRequired, AgeInvalid).Error);
    }

    // Typical use: every field of a form, reported per field.

    [Fact]
    public void FormValidation_ReportsEveryInvalidField()
    {
        static Result<string> Required(string? value, string field) =>
            string.IsNullOrWhiteSpace(value)
                ? Error.Validation($"{field} is required.").WithField(field)
                : value;

        var result = Result.CombineAll([Required("", "email"), Required("ann", "name"), Required(null, "password")]);

        var byField = result.Error.Details.ToDictionary(e => e.Field!, e => e.Message);
        Assert.Equal(new Dictionary<string, string>
        {
            ["email"] = "email is required.",
            ["password"] = "password is required.",
        }, byField);
    }
}
