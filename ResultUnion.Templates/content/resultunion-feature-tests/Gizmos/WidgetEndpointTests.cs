using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace TargetNamespace.Gizmos;

/// <summary>The application in memory, called over HTTP: what a client receives, problem responses included.</summary>
public sealed class WidgetEndpointTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

#if (Sqlite)
    // A database file of its own, so the tests never touch the application's. "Products" is the connection string of
    // resultunion-api --database sqlite; use the application's own name.
    private readonly HttpClient _client = factory
        .WithWebHostBuilder(host => host.UseSetting("ConnectionStrings:Products", $"Data Source={Path.Combine(Path.GetTempPath(), $"PLURAL_ROUTE-{Guid.NewGuid():N}.db")}"))
        .CreateClient();
#else
    private readonly HttpClient _client = factory.CreateClient();
#endif

    [Fact]
    public async Task A_created_ENTITY_CODE_is_201_and_found_at_its_location()
    {
        var name = $"First {Guid.NewGuid()}";

        var created = await _client.PostAsJsonAsync("/PLURAL_ROUTE", new { name }, Ct);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var found = await _client.GetFromJsonAsync<JsonElement>(created.Headers.Location, Ct);
        Assert.Equal(name, found.GetProperty("name").GetString());
    }

    [Fact]
    public async Task An_empty_name_is_400_with_its_code()
    {
        var response = await _client.PostAsJsonAsync("/PLURAL_ROUTE", new { name = " " }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("ENTITY_CODE_name.required", await CodeOf(response));
    }

    [Fact]
    public async Task A_name_that_is_taken_is_409()
    {
        var name = $"First {Guid.NewGuid()}";
        await _client.PostAsJsonAsync("/PLURAL_ROUTE", new { name }, Ct);

        var response = await _client.PostAsJsonAsync("/PLURAL_ROUTE", new { name = name.ToUpperInvariant() }, Ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("ENTITY_CODE.name_taken", await CodeOf(response));
    }

    [Fact]
    public async Task A_missing_ENTITY_CODE_is_404()
    {
        var response = await _client.GetAsync($"/PLURAL_ROUTE/{Guid.NewGuid()}", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("ENTITY_CODE.not_found", await CodeOf(response));
    }

    private static async Task<string?> CodeOf(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString();
}
