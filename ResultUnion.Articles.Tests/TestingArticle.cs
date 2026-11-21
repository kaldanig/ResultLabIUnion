// The code of docs/articles/testing.md, verbatim, as runnable tests.
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ResultUnion.Articles.Tests;

public sealed class FakeUserRepository(string existing) : IUserRepository
{
    private readonly InMemoryUsers _inner = new(existing);
    public Task<Result> EnsureEmailIsFreeAsync(Email email, CancellationToken ct) => _inner.EnsureEmailIsFreeAsync(email, ct);
    public Task AddAsync(User user, CancellationToken ct) => _inner.AddAsync(user, ct);
}

public sealed class TestingArticle
{
    [Fact]
    public void An_empty_email_is_required()
    {
        var result = Email.Create("  ");

        Assert.True(result.TryGetError(out var error));
        Assert.Equal("email.required", error.Code);
        Assert.Equal(ErrorType.Validation, error.Type);
    }

    [Fact]
    public void Catalog_comparison()
    {
        Assert.True(Email.Create(null).TryGetError(out var error));
        Assert.Equal(Email.Errors.Required, error);
    }

    [Fact]
    public void Arguments()
    {
        Assert.True(Email.Create("ann.example.com").TryGetError(out var error));
        Assert.Equal("email.invalid_format", error.Code);
        Assert.Equal("ann.example.com", error.Arguments["value"]);
    }

    [Fact]
    public void Value()
    {
        var result = Email.Create(" Ann@Example.com ");

        Assert.True(result.TryGetResultValue(out var email), result.IsFailure ? result.Error.Code : null);
        Assert.Equal("ann@example.com", email.Value);
    }

    [Theory]
    [InlineData("ann@example.com", "ann@example.com")]
    [InlineData("", "email.required")]
    public void Switch(string input, string expected)
    {
        var text = Email.Create(input) switch
        {
            Email email => email.Value,
            Error error => error.Code,
        };
        Assert.Equal(expected, text);
    }

    [Fact]
    public void Several_errors()
    {
        var result = UserService.Validate(new RegisterUser(Email: "", Password: "short", Age: "34"));

        Assert.True(result.TryGetError(out var error));
        Assert.Equal(
            new Dictionary<string, string> { ["email"] = "email.required", ["password"] = "password.too_short" },
            error.Flatten().ToDictionary(e => e.Field!, e => e.Code));
    }

    [Fact]
    public async Task A_taken_email_is_a_conflict()
    {
        var service = new UserService(new FakeUserRepository(existing: "ann@example.com"));

        var result = await service.RegisterAsync(new RegisterUser("ann@example.com", "secret123", "34"), CancellationToken.None);

        Assert.True(result.TryGetError(out var error));
        Assert.Equal(User.Errors.EmailTaken("ann@example.com"), error);
    }
}

public sealed class RegisterEndpointTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task An_invalid_registration_is_a_validation_problem()
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await factory.CreateClient().PostAsJsonAsync("/users", new { email = "", password = "secret123", age = "34" }, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("email.required", problem.GetProperty("code").GetString());
    }
}
