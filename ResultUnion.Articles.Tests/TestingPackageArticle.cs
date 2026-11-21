using ResultUnion.Testing;

namespace ResultUnion.Articles.Tests;

public sealed class TestingPackageArticle
{
    [Fact]
    public void An_empty_email_is_required() =>
        Email.Create("  ").ShouldFail("email.required").ShouldHaveType(ErrorType.Validation);

    [Fact]
    public void A_valid_email_is_normalized() =>
        Assert.Equal("ann@example.com", Email.Create(" Ann@Example.com ").ShouldSucceed().Value);

    [Fact]
    public void Several_errors_are_reported_at_once() =>
        UserService.Validate(new RegisterUser(Email: "", Password: "short", Age: "34")).ShouldFail()
            .ShouldHaveFieldErrors(("email", "email.required"), ("password", "password.too_short"));

    [Fact]
    public async Task A_taken_email_is_a_conflict()
    {
        var service = new UserService(new FakeUserRepository(existing: "ann@example.com"));

        await service.RegisterAsync(new RegisterUser("ann@example.com", "secret123", "34"), CancellationToken.None)
            .ShouldFailAsync("user.email_taken");
    }

    [Fact]
    public void The_documented_failure_message()
    {
        var ex = Assert.Throws<ResultAssertionException>(() => Email.Create("ann").ShouldFail("email.required"));
        Assert.Equal("Expected the error code 'email.required', but the error is email.invalid_format: 'ann' is not a valid email. (Validation, arguments value=\"ann\").", ex.Message);
    }
}
