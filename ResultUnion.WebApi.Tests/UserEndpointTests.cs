using System.Net;
using static ResultUnion.WebApi.Tests.Http;

namespace ResultUnion.WebApi.Tests;

public sealed class UserEndpointTests(WebApiFactory factory) : IClassFixture<WebApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Registering_a_valid_user_returns_201_with_the_dto_and_a_location()
    {
        var email = NewEmail();

        var (response, body) = await _client.SendAsync(HttpMethod.Post, "/users", new { email, password = "secret123", age = "30" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal($"/users/{email}", response.Headers.Location?.OriginalString);
        Assert.Equal(email, body.GetProperty("email").GetString());
        Assert.Equal(30, body.GetProperty("age").GetInt32());
        Assert.True(body.GetProperty("isActive").GetBoolean());
        Assert.False(body.TryGetProperty("isSuccess", out _));          // a DTO, never the serialized Result
    }

    [Fact]
    public async Task Every_invalid_field_is_reported_at_once_as_a_validation_problem()
    {
        var (response, problem) = await _client.SendAsync(HttpMethod.Post, "/users", new { email = "bob@", password = "123", age = "abc" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(400, problem.GetProperty("status").GetInt32());
        Assert.Equal("validation.failed", problem.GetProperty("code").GetString());

        Assert.Equal(["age", "email", "password"], problem.Errors().Keys.Order());
        Assert.Equal(
            new Dictionary<string, string>
            {
                ["email"] = "email.invalid_format",
                ["password"] = "password.too_short",
                ["age"] = "user.age_not_a_number",
            },
            problem.DetailCodes());
    }

    [Fact]
    public async Task A_business_rule_is_422_with_its_code_and_arguments()
    {
        var (response, problem) = await _client.SendAsync(HttpMethod.Post, "/users", new { email = NewEmail(), password = "secret123", age = "15" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("user.too_young", problem.GetProperty("code").GetString());
        var arguments = problem.GetProperty("arguments");
        Assert.Equal(18, arguments.GetProperty("minimumAge").GetInt32());
        Assert.Equal(15, arguments.GetProperty("age").GetInt32());
    }

    [Fact]
    public async Task A_duplicate_email_is_409_conflict()
    {
        var (response, problem) = await _client.SendAsync(HttpMethod.Post, "/users", new { email = "ann@example.com", password = "secret123", age = "40" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("user.email_already_registered", problem.GetProperty("code").GetString());
        Assert.Equal("ann@example.com", problem.GetProperty("arguments").GetProperty("email").GetString());
    }

    [Fact]
    public async Task Accept_Language_ka_translates_the_messages_from_code_and_arguments()
    {
        var (tooYoung, problem) = await _client.SendAsync(
            HttpMethod.Post, "/users", new { email = NewEmail(), password = "secret123", age = "15" }, language: "ka");

        Assert.Equal("ka", tooYoung.Content.Headers.ContentLanguage.Single());
        Assert.Equal("მომხმარებელი უნდა იყოს მინიმუმ 18 წლის, არის 15.", problem.GetProperty("detail").GetString());
        Assert.Equal("user.too_young", problem.GetProperty("code").GetString());            // the code never changes

        var (_, invalid) = await _client.SendAsync(
            HttpMethod.Post, "/users", new { email = "bob@", password = "secret123", age = "30" }, language: "ka");
        Assert.Equal("„bob@“ არ არის სწორი ელფოსტა.", invalid.Errors()["email"].Single());
    }

    [Fact]
    public async Task Finding_a_missing_user_is_404_and_an_invalid_email_is_400_on_its_field()
    {
        var (missing, notFound) = await _client.SendAsync(HttpMethod.Get, $"/users/{NewEmail()}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("user.not_found", notFound.GetProperty("code").GetString());

        var (invalid, problem) = await _client.SendAsync(HttpMethod.Get, "/users/not-an-email");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal("email.invalid_format", problem.DetailCodes()["email"]);
    }

    [Fact]
    public async Task Deactivating_is_204_then_409_the_second_time()
    {
        var email = NewEmail();
        await _client.RegisterAsync(email);

        var (first, _) = await _client.SendAsync(HttpMethod.Delete, $"/users/{email}");
        var (second, problem) = await _client.SendAsync(HttpMethod.Delete, $"/users/{email}");

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("user.already_inactive", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task The_fourth_password_reset_within_a_minute_is_429_with_retryAfterSeconds()
    {
        var email = NewEmail();
        await _client.RegisterAsync(email);

        for (var i = 0; i < 3; i++)
        {
            var (accepted, _) = await _client.SendAsync(HttpMethod.Post, $"/users/{email}/password-reset");
            Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        }

        var (limited, problem) = await _client.SendAsync(HttpMethod.Post, $"/users/{email}/password-reset");

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal("user.password_reset_limit", problem.GetProperty("code").GetString());
        var retryAfter = problem.GetProperty("arguments").GetProperty("retryAfterSeconds").GetInt32();
        Assert.InRange(retryAfter, 1, 60);
    }
}
