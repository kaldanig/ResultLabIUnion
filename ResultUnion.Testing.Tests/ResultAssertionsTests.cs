using ResultUnion.Testing;

namespace ResultUnion.Testing.Tests;

public sealed class ResultAssertionsTests
{
    private static readonly Error Required =
        Error.Create("email.required", "Email is required.", ErrorType.Validation).WithField("email");
    private static readonly Error TooShort =
        Error.Create("password.too_short", "At least 8.", ErrorType.Validation).WithField("password").WithArgument("minimumLength", 8);
    private static readonly Error Taken =
        Error.Create("user.email_taken", "Taken.", ErrorType.Conflict).WithArgument("email", "ann@example.com");

    private static Result<int> Five => 5;
    private static Result<int> Missing => Required;

    private static string Message(Action assertion) => Assert.Throws<ResultAssertionException>(assertion).Message;

    // ---- Result<T> -------------------------------------------------------------------------------------------------

    [Fact]
    public void ShouldSucceed_returns_the_value_or_fails_with_the_whole_error()
    {
        Assert.Equal(5, Five.ShouldSucceed());

        var message = Message(() => Missing.ShouldSucceed());
        Assert.Equal("Expected a success, but the result failed with email.required: Email is required. (field 'email') (Validation).", message);
    }

    [Fact]
    public void ShouldFail_returns_the_error_or_fails_with_the_value()
    {
        Assert.Same(Required, Missing.ShouldFail());
        Assert.Same(Required, Missing.ShouldFail("email.required"));

        Assert.Equal("Expected a failure, but the result succeeded with 5.", Message(() => Five.ShouldFail()));
        Assert.Equal("Expected a failure, but the result succeeded with \"ann\".", Message(() => Result<string>.Success("ann").ShouldFail()));
        Assert.Contains("Expected a failure", Message(() => Five.ShouldFail("email.required")), StringComparison.Ordinal);
        Assert.Equal(
            "Expected the error code 'user.email_taken', but the error is email.required: Email is required. (field 'email') (Validation).",
            Message(() => Missing.ShouldFail("user.email_taken")));
    }

    // ---- Result ----------------------------------------------------------------------------------------------------

    [Fact]
    public void The_same_assertions_work_on_a_result_without_a_value()
    {
        Result.Success().ShouldSucceed();
        Assert.Same(Taken, Result.Failure(Taken).ShouldFail());
        Assert.Same(Taken, Result.Failure(Taken).ShouldFail("user.email_taken"));

        Assert.Equal(
            "Expected a success, but the result failed with user.email_taken: Taken. (Conflict, arguments email=\"ann@example.com\").",
            Message(() => Result.Failure(Taken).ShouldSucceed()));
        Assert.Equal("Expected a failure, but the result succeeded.", Message(() => Result.Success().ShouldFail()));
        Assert.Contains("Expected the error code 'other'", Message(() => Result.Failure(Taken).ShouldFail("other")), StringComparison.Ordinal);
    }

    // ---- async -----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_async_forms_await_the_result_first()
    {
        Assert.Equal(5, await Task.FromResult(Five).ShouldSucceedAsync());
        Assert.Same(Required, await Task.FromResult(Missing).ShouldFailAsync());
        Assert.Same(Required, await Task.FromResult(Missing).ShouldFailAsync("email.required"));
        await Task.FromResult(Result.Success()).ShouldSucceedAsync();
        Assert.Same(Taken, await Task.FromResult(Result.Failure(Taken)).ShouldFailAsync());
        Assert.Same(Taken, await Task.FromResult(Result.Failure(Taken)).ShouldFailAsync("user.email_taken"));

        await Assert.ThrowsAsync<ResultAssertionException>(() => Task.FromResult(Missing).ShouldSucceedAsync());
        await Assert.ThrowsAsync<ResultAssertionException>(() => Task.FromResult(Five).ShouldFailAsync());
        await Assert.ThrowsAsync<ResultAssertionException>(() => Task.FromResult(Missing).ShouldFailAsync("other"));
        await Assert.ThrowsAsync<ResultAssertionException>(() => Task.FromResult(Result.Failure(Taken)).ShouldSucceedAsync());
        await Assert.ThrowsAsync<ResultAssertionException>(() => Task.FromResult(Result.Success()).ShouldFailAsync());
        await Assert.ThrowsAsync<ResultAssertionException>(() => Task.FromResult(Result.Failure(Taken)).ShouldFailAsync("other"));
    }

    [Fact]
    public void A_blank_code_is_reported_before_the_result_is_looked_at()
    {
        Assert.Throws<ArgumentException>(() => Five.ShouldFail(" "));
        Assert.Throws<ArgumentException>(() => Result.Success().ShouldFail(" "));
    }

    [Fact]
    public void Messages_list_arguments_in_name_order_and_show_null()
    {
        var error = Error.Validation("Invalid.").WithArgument("zeta", 2).WithArgument("alpha", 1);

        Assert.Equal(
            "Expected the argument 'beta', but the error has arguments alpha=1, zeta=2: validation.failed: Invalid. (Validation, arguments alpha=1, zeta=2).",
            Message(() => error.ShouldHaveArgument("beta", 1)));
        Assert.Equal("Expected the argument 'alpha' to be null, but it is 1.", Message(() => error.ShouldHaveArgument("alpha", null)));
        Assert.Equal("Expected the argument 'pair' to be null, but it is [a, 1].",   // not IFormattable: its ToString
            Message(() => error.WithArgument("pair", KeyValuePair.Create("a", 1)).ShouldHaveArgument("pair", null)));
    }

    [Fact]
    public void Numbers_that_decimal_cannot_hold_compare_without_crashing()
    {
        var error = Error.Validation("x").WithArgument("big", 1e30).WithArgument("inf", double.PositiveInfinity).WithArgument("finf", float.NegativeInfinity);

        error.ShouldHaveArgument("big", 1e30).ShouldHaveArgument("inf", double.PositiveInfinity).ShouldHaveArgument("finf", float.NegativeInfinity);
        Assert.Throws<ResultAssertionException>(() => error.ShouldHaveArgument("big", 1));
        Assert.Throws<ResultAssertionException>(() => error.ShouldHaveArgument("inf", 1));
        Assert.Throws<ResultAssertionException>(() => error.ShouldHaveArgument("finf", 1));
        Assert.Throws<ResultAssertionException>(() => Error.Validation("x").WithArgument("f", 1e30f).ShouldHaveArgument("f", 1));
    }

    [Fact]
    public void A_blank_code_is_a_mistake_in_the_test()
    {
        Assert.Throws<ArgumentException>(() => Missing.ShouldFail(" "));
        Assert.Throws<ArgumentException>(() => Result.Failure(Taken).ShouldFail(""));
        Assert.Throws<ArgumentException>(() => { _ = Task.FromResult(Missing).ShouldFailAsync(" "); });
        Assert.Throws<ArgumentException>(() => { _ = Task.FromResult(Result.Failure(Taken)).ShouldFailAsync(" "); });
        Assert.Throws<ArgumentException>(() => Required.ShouldHaveCode(" "));
        Assert.Throws<ArgumentException>(() => Required.ShouldHaveArgument(" ", 1));
    }

    // ---- Error -----------------------------------------------------------------------------------------------------

    [Fact]
    public void Error_assertions_chain_and_name_what_differs()
    {
        Assert.Same(TooShort, TooShort.ShouldHaveCode("password.too_short").ShouldHaveType(ErrorType.Validation)
            .ShouldHaveField("password").ShouldHaveArgument("minimumLength", 8));
        Assert.Same(Taken, Taken.ShouldHaveField(null));

        Assert.Contains("Expected an error of type NotFound, but the error is user.email_taken", Message(() => Taken.ShouldHaveType(ErrorType.NotFound)), StringComparison.Ordinal);
        Assert.Contains("Expected the field 'email', but the error is user.email_taken", Message(() => Taken.ShouldHaveField("email")), StringComparison.Ordinal);
        Assert.Contains("Expected no field, but the error is email.required", Message(() => Required.ShouldHaveField(null)), StringComparison.Ordinal);
    }

    [Fact]
    public void Arguments_compare_numbers_by_value_and_report_what_is_there()
    {
        TooShort.ShouldHaveArgument("minimumLength", 8L).ShouldHaveArgument("minimumLength", 8.0).ShouldHaveArgument("minimumLength", 8m);
        Error.Validation("x").WithArgument("ratio", 0.5).ShouldHaveArgument("ratio", 0.5f);
        Error.Validation("x").WithArgument("none", null).ShouldHaveArgument("none", null);

        Assert.Equal("Expected the argument 'minimumLength' to be 9, but it is 8.", Message(() => TooShort.ShouldHaveArgument("minimumLength", 9)));
        Assert.Equal("Expected the argument 'email' to be \"bob\", but it is \"ann@example.com\".", Message(() => Taken.ShouldHaveArgument("email", "bob")));
        Assert.Contains("Expected the argument 'age', but the error has arguments email=\"ann@example.com\"", Message(() => Taken.ShouldHaveArgument("age", 1)), StringComparison.Ordinal);
        Assert.Contains("but the error has no arguments", Message(() => Required.ShouldHaveArgument("age", 1)), StringComparison.Ordinal);
        Assert.Throws<ResultAssertionException>(() => Error.Validation("x").WithArgument("ratio", double.NaN).ShouldHaveArgument("ratio", 0));
        Assert.Throws<ResultAssertionException>(() => TooShort.ShouldHaveArgument("minimumLength", "8"));
    }

    [Fact]
    public void Field_errors_compare_as_a_set_and_list_what_is_missing_and_extra()
    {
        var group = Error.Validation([Required, TooShort]);

        group.ShouldHaveFieldErrors(("password", "password.too_short"), ("email", "email.required"));
        Required.ShouldHaveFieldErrors(("email", "email.required"));
        Taken.ShouldHaveFieldErrors((null, "user.email_taken"));

        Assert.Equal(
            "The field errors are not the expected ones. Missing: age → age.invalid. Not expected: password → password.too_short. "
            + "The error is validation.failed: One or more validation errors occurred. [email.required: Email is required. (field 'email'); "
            + "password.too_short: At least 8. (field 'password')] (Validation).",
            Message(() => group.ShouldHaveFieldErrors(("email", "email.required"), ("age", "age.invalid"))));

        var message = Message(() => group.ShouldHaveFieldErrors(("email", "email.required"), ("age", "age.invalid")));
        Assert.Contains("Missing: age → age.invalid.", message, StringComparison.Ordinal);
        Assert.Contains("Not expected: password → password.too_short.", message, StringComparison.Ordinal);
        Assert.DoesNotContain("Missing: email", message, StringComparison.Ordinal);
        Assert.Contains("Not expected: (no field) → user.email_taken.", Message(() => Taken.ShouldHaveFieldErrors()), StringComparison.Ordinal);
        Assert.DoesNotContain("Not expected", Message(() => Required.ShouldHaveFieldErrors(("email", "email.required"), ("age", "age.invalid"))), StringComparison.Ordinal);
    }
}
