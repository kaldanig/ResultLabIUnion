namespace ResultUnion.Tests;

/// <summary><c>Result.CombineAll</c> for results with values, and the tuple overloads of <c>Map</c> and <c>Bind</c>.</summary>
public class CombineAllValuesTests
{
    private static readonly Error EmailInvalid = Error.Validation("Email is invalid.").WithField("email");
    private static readonly Error AgeInvalid = Error.Validation("Age is invalid.").WithField("age");
    private static readonly Error NameMissing = Error.Validation("Name is required.").WithField("name");

    private sealed record Account(string Email, int Age);

    private static Account CreateAccount(string email, int age) => new(email, age);

    private static Result<Account> TryCreateAccount(string email, int age) =>
        age >= 18 ? CreateAccount(email, age) : Error.BusinessRule("Too young.");

    // Two values

    [Fact]
    public void Two_AllSucceed_ReturnsBothValues()
    {
        var result = Result.CombineAll(Result<string>.Success("ann@example.com"), Result<int>.Success(34));

        Assert.Equal(("ann@example.com", 34), result.ResultValue);
    }

    [Fact]
    public void Two_SingleFailure_IsReturnedUnchanged()
    {
        var result = Result.CombineAll(Result<string>.Success("ann@example.com"), Result<int>.Failure(AgeInvalid));

        Assert.Same(AgeInvalid, result.Error);
    }

    [Fact]
    public void Two_BothFail_AreGroupedInArgumentOrder()
    {
        var error = Result.CombineAll(Result<string>.Failure(EmailInvalid), Result<int>.Failure(AgeInvalid)).Error;

        Assert.Equal("validation.failed", error.Code);
        Assert.Equal([EmailInvalid, AgeInvalid], error.Details);
    }

    [Fact]
    public void Two_DefaultResult_CountsAsFailure()
    {
        Assert.Same(Error.DefaultResult, Result.CombineAll(Result<int>.Success(1), default(Result<string>)).Error);
    }

    // Three and four values

    [Fact]
    public void Three_AllSucceedOrEveryErrorReported()
    {
        Assert.Equal(("a", 1, true), Result.CombineAll(Result<string>.Success("a"), Result<int>.Success(1), Result<bool>.Success(true)).ResultValue);

        var error = Result.CombineAll(Result<string>.Failure(EmailInvalid), Result<int>.Success(1), Result<string>.Failure(NameMissing)).Error;
        Assert.Equal([EmailInvalid, NameMissing], error.Details);
    }

    [Fact]
    public void Four_AllSucceedOrEveryErrorReported()
    {
        var values = Result.CombineAll(Result<int>.Success(1), Result<int>.Success(2), Result<int>.Success(3), Result<int>.Success(4)).ResultValue;
        Assert.Equal((1, 2, 3, 4), values);

        var error = Result.CombineAll(
            Result<string>.Failure(EmailInvalid), Result<int>.Failure(AgeInvalid), Result<string>.Failure(NameMissing), Result<int>.Success(4)).Error;
        Assert.Equal([EmailInvalid, AgeInvalid, NameMissing], error.Details);
    }

    // Tuple Map / Bind: one parameter per value, method groups included.

    [Fact]
    public void Map_TakesOneParameterPerValue()
    {
        var account = Result.CombineAll(Result<string>.Success("ann@example.com"), Result<int>.Success(34))
            .Map(CreateAccount);                                         // method group: (string, int) -> Account

        Assert.Equal(new Account("ann@example.com", 34), account.ResultValue);
    }

    [Fact]
    public void Bind_TakesOneParameterPerValue_AndCanFail()
    {
        var combined = Result.CombineAll(Result<string>.Success("bob@example.com"), Result<int>.Success(15));

        Assert.Equal(ErrorType.BusinessRule, combined.Bind(TryCreateAccount).Error.Type);
        Assert.Equal(34, Result.CombineAll(Result<string>.Success("a"), Result<int>.Success(34)).Bind(TryCreateAccount).ResultValue.Age);
    }

    [Fact]
    public void MapAndBind_Failure_DoNotInvokeTheDelegate()
    {
        var failed = Result.CombineAll(Result<string>.Failure(EmailInvalid), Result<int>.Failure(AgeInvalid));

        Assert.Equal(2, failed.Map<string, int, string>((_, _) => throw new InvalidOperationException()).Error.Details.Count);
        Assert.Equal(2, failed.Bind<string, int, Account>((_, _) => throw new InvalidOperationException()).Error.Details.Count);
    }

    [Fact]
    public void MapAndBind_ThreeAndFourValues()
    {
        var three = Result.CombineAll(Result<int>.Success(1), Result<int>.Success(2), Result<int>.Success(3));
        var four = Result.CombineAll(Result<int>.Success(1), Result<int>.Success(2), Result<int>.Success(3), Result<int>.Success(4));

        Assert.Equal(6, three.Map((a, b, c) => a + b + c).ResultValue);
        Assert.Equal(6, three.Bind((a, b, c) => Result<int>.Success(a + b + c)).ResultValue);
        Assert.Equal(10, four.Map((a, b, c, d) => a + b + c + d).ResultValue);
        Assert.Equal(10, four.Bind((a, b, c, d) => Result<int>.Success(a + b + c + d)).ResultValue);
    }

    [Fact]
    public void MapAndBind_NullDelegate_Throws()
    {
        var two = Result.CombineAll(Result<int>.Success(1), Result<int>.Success(2));

        Assert.Throws<ArgumentNullException>(() => two.Map((Func<int, int, int>)null!));
        Assert.Throws<ArgumentNullException>(() => two.Bind((Func<int, int, Result<int>>)null!));
    }

    [Fact]
    public void SingleParameterMap_StillWorksOnTheTuple()
    {
        var two = Result.CombineAll(Result<int>.Success(1), Result<int>.Success(2));

        Assert.Equal(3, two.Map(values => values.Item1 + values.Item2).ResultValue);
    }

    // Typical command handler: value objects for every field, all errors reported per field.

    [Theory]
    [InlineData("ann@example.com", 34, "ok:ann@example.com")]
    [InlineData("bad", -1, "error:email,age")]
    [InlineData("ann@example.com", 15, "error:business_rule.violated")]
    public void CommandHandler_CreatesTheEntityOrReportsEveryField(string email, int age, string expected)
    {
        Result<string> Email(string value) => value.Contains('@') ? value : EmailInvalid;
        Result<int> Age(int value) => value >= 0 ? value : AgeInvalid;

        var outcome = Result.CombineAll(Email(email), Age(age))
            .Bind(TryCreateAccount)
            .Match(
                account => $"ok:{account.Email}",
                error => error.Details.Count > 0
                    ? $"error:{string.Join(",", error.Details.Select(d => d.Field))}"
                    : $"error:{error.Code}");

        Assert.Equal(expected, outcome);
    }
}
