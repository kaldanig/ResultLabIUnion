using System.Net;
using System.Text.Json;
using static ResultUnion.WebApi.Tests.Http;

namespace ResultUnion.WebApi.Tests;

public sealed class OrderEndpointTests(WebApiFactory factory) : IClassFixture<WebApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task A_valid_order_is_201_with_its_lines()
    {
        var order = new
        {
            customerEmail = "ann@example.com",
            lines = new[] { new { product = "apple", quantity = 2 }, new { product = "pear", quantity = 3 } },
        };

        var (response, body) = await _client.SendAsync(HttpMethod.Post, "/orders", order);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(5, body.GetProperty("totalQuantity").GetInt32());
        Assert.Equal(2, body.GetProperty("lines").GetArrayLength());
    }

    [Fact]
    public async Task Each_invalid_line_is_reported_with_its_index()
    {
        var order = new
        {
            customerEmail = "ann@example.com",
            lines = new[] { new { product = "apple", quantity = 2 }, new { product = "", quantity = 0 } },
        };

        var (response, problem) = await _client.SendAsync(HttpMethod.Post, "/orders", order);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            new Dictionary<string, string>
            {
                ["lines[1].product"] = "order_line.product_required",
                ["lines[1].quantity"] = "order_line.quantity_not_positive",
            },
            problem.DetailCodes());
    }

    [Fact]
    public async Task An_unknown_customer_is_404_on_the_customerEmail_field()
    {
        var order = new { customerEmail = NewEmail(), lines = new[] { new { product = "apple", quantity = 1 } } };

        var (response, problem) = await _client.SendAsync(HttpMethod.Post, "/orders", order);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("user.not_found", problem.GetProperty("code").GetString());
        Assert.Equal("customerEmail", problem.DetailCodes().Keys.Single());
    }
}

public sealed class AdminEndpointTests(WebApiFactory factory) : IClassFixture<WebApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Without_the_admin_role_the_list_is_403_forbidden()
    {
        var (response, problem) = await _client.SendAsync(HttpMethod.Get, "/admin/users", role: "user");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("forbidden", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task With_the_admin_role_the_list_includes_the_seeded_user()
    {
        var (response, body) = await _client.SendAsync(HttpMethod.Get, "/admin/users", role: "admin");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(body.EnumerateArray(), user => user.GetProperty("email").GetString() == "ann@example.com");
    }
}

/// <summary>The OpenAPI document lists every response: the typed success from ToHttpResult, the problems from ProducesErrors.</summary>
public sealed class OpenApiDocumentTests(WebApiFactory factory) : IClassFixture<WebApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Theory]
    [InlineData("/users", "post", new[] { "201", "400", "409", "422" })]
    [InlineData("/users/{email}", "get", new[] { "200", "400", "404" })]
    [InlineData("/users/{email}", "delete", new[] { "204", "400", "404", "409" })]
    [InlineData("/users/{email}/password-reset", "post", new[] { "202", "400", "404", "429" })]
    [InlineData("/orders", "post", new[] { "201", "400", "404" })]
    [InlineData("/admin/users", "get", new[] { "200", "403" })]
    public async Task Each_endpoint_documents_its_success_and_problem_responses(string path, string method, string[] statuses)
    {
        var (response, document) = await _client.SendAsync(HttpMethod.Get, "/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var responses = document.GetProperty("paths").GetProperty(path).GetProperty(method).GetProperty("responses");

        Assert.Equal(statuses, responses.EnumerateObject().Select(status => status.Name).Order());
        foreach (var problem in responses.EnumerateObject().Where(status => status.Name.StartsWith('4')))
        {
            Assert.True(problem.Value.GetProperty("content").TryGetProperty("application/problem+json", out _),
                $"{method} {path} {problem.Name} is not documented as problem details");
        }
    }

    [Fact]
    public async Task A_success_response_is_documented_with_its_dto_schema()
    {
        var (_, document) = await _client.SendAsync(HttpMethod.Get, "/openapi/v1.json");

        var created = document.GetProperty("paths").GetProperty("/users").GetProperty("post")
            .GetProperty("responses").GetProperty("201").GetProperty("content").GetProperty("application/json").GetProperty("schema");

        Assert.EndsWith("UserResponse", created.GetProperty("$ref").GetString());
    }
}
