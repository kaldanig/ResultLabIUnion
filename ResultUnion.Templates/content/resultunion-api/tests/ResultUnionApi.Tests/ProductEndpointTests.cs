using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ResultUnionApi.Tests;

/// <summary>The application in memory, called over HTTP: what a client receives, problem responses included.</summary>
public sealed class ProductEndpointTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

#if (Sqlite)
    // A database file of its own, so the tests never touch the application's.
    private readonly HttpClient _client = factory
        .WithWebHostBuilder(host => host.UseSetting("ConnectionStrings:Products", $"Data Source={Path.Combine(Path.GetTempPath(), $"products-{Guid.NewGuid():N}.db")}"))
        .CreateClient();
#else
    private readonly HttpClient _client = factory.CreateClient();
#endif

    [Fact]
    public async Task A_valid_product_is_201_with_its_location()
    {
        var response = await _client.PostAsJsonAsync("/products", new { name = $"Coffee {Guid.NewGuid()}", price = 4.5m }, Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal($"/products/{body.GetProperty("id").GetGuid()}", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Invalid_input_is_a_400_problem_with_a_code_per_field()
    {
        var response = await _client.PostAsJsonAsync("/products", new { name = "", price = -1 }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("validation.failed", problem.GetProperty("code").GetString());
        var details = problem.GetProperty("details").EnumerateArray()
            .ToDictionary(d => d.GetProperty("field").GetString()!, d => d.GetProperty("code").GetString());
        Assert.Equal(new Dictionary<string, string?> { ["name"] = "product_name.required", ["price"] = "price.not_positive" }, details);
    }

    [Fact]
    public async Task An_unknown_product_is_a_404_problem()
    {
        var response = await _client.GetAsync($"/products/{Guid.NewGuid()}", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("product.not_found", problem.GetProperty("code").GetString());
    }
}
