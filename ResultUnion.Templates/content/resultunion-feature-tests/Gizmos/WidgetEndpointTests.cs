using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
#if (Crud)
using Microsoft.Extensions.DependencyInjection;
using APP_NAMESPACE.Gizmos;
using ResultUnion.Testing;
#endif
using Xunit;

namespace TargetNamespace.Gizmos;

/// <summary>The application in memory, called over HTTP: what a client receives, problem responses included.</summary>
public sealed class WidgetEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly WebApplicationFactory<Program> _app;
    private readonly HttpClient _client;

    public WidgetEndpointTests(WebApplicationFactory<Program> factory)
    {
#if (Sqlite)
        // A database file of its own, so the tests never touch the application's. "Products" is the connection string
        // of resultunion-api --database sqlite; use the application's own name.
        _app = factory.WithWebHostBuilder(host =>
            host.UseSetting("ConnectionStrings:Products", $"Data Source={Path.Combine(Path.GetTempPath(), $"PLURAL_ROUTE-{Guid.NewGuid():N}.db")}"));
#else
        _app = factory;
#endif
        _client = _app.CreateClient();
    }

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

#if (Crud)
    [Fact]
    public async Task A_rename_is_200_with_the_next_version_and_a_stale_one_is_409()
    {
        var created = await (await _client.PostAsJsonAsync("/PLURAL_ROUTE", new { name = $"First {Guid.NewGuid()}" }, Ct))
            .Content.ReadFromJsonAsync<JsonElement>(Ct);
        var id = created.GetProperty("id").GetGuid();
        var version = created.GetProperty("version").GetInt32();

        var renamed = await _client.PutAsJsonAsync($"/PLURAL_ROUTE/{id}", new { name = $"Second {Guid.NewGuid()}", version }, Ct);
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        Assert.Equal(version + 1, (await renamed.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("version").GetInt32());

        var stale = await _client.PutAsJsonAsync($"/PLURAL_ROUTE/{id}", new { name = $"Third {Guid.NewGuid()}", version }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("ENTITY_CODE.changed_concurrently", await CodeOf(stale));
    }

    [Fact]
    public async Task A_delete_is_204_and_then_404()
    {
        var created = await (await _client.PostAsJsonAsync("/PLURAL_ROUTE", new { name = $"First {Guid.NewGuid()}" }, Ct))
            .Content.ReadFromJsonAsync<JsonElement>(Ct);
        var id = created.GetProperty("id").GetGuid();
        var version = created.GetProperty("version").GetInt32();

        var deleted = await _client.DeleteAsync($"/PLURAL_ROUTE/{id}?version={version}", Ct);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        var again = await _client.DeleteAsync($"/PLURAL_ROUTE/{id}?version={version}", Ct);
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
        Assert.Equal("ENTITY_CODE.not_found", await CodeOf(again));
    }

    [Fact]
    public async Task A_rename_to_a_taken_name_is_409()
    {
        var taken = $"First {Guid.NewGuid()}";
        await _client.PostAsJsonAsync("/PLURAL_ROUTE", new { name = taken }, Ct);
        var other = await (await _client.PostAsJsonAsync("/PLURAL_ROUTE", new { name = $"Second {Guid.NewGuid()}" }, Ct))
            .Content.ReadFromJsonAsync<JsonElement>(Ct);

        var response = await _client.PutAsJsonAsync(
            $"/PLURAL_ROUTE/{other.GetProperty("id").GetGuid()}",
            new { name = taken.ToUpperInvariant(), version = other.GetProperty("version").GetInt32() }, Ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("ENTITY_CODE.name_taken", await CodeOf(response));
    }

    // Two requests read the same version; the second one's write comes after the first one's. The service's check saw
    // the version it read, so only the store, which compares as it writes, can refuse it.
    [Fact]
    public async Task The_store_refuses_a_write_from_a_version_that_changed_after_it_was_read()
    {
        var created = await (await _client.PostAsJsonAsync("/PLURAL_ROUTE", new { name = $"First {Guid.NewGuid()}" }, Ct))
            .Content.ReadFromJsonAsync<JsonElement>(Ct);
        var id = created.GetProperty("id").GetGuid();
        using var scope = _app.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IWidgetRepository>();
        var read = await repository.FindAsync(id, Ct) ?? throw new InvalidOperationException("Not stored.");

        await _client.PutAsJsonAsync($"/PLURAL_ROUTE/{id}", new { name = $"Second {Guid.NewGuid()}", version = read.Version }, Ct);

        var changed = read.Rename(WidgetName.Create($"Third {Guid.NewGuid()}").ShouldSucceed());
        await repository.UpdateAsync(changed, read.Version, Ct).ShouldFailAsync("ENTITY_CODE.changed_concurrently");
        await repository.RemoveAsync(id, read.Version, Ct).ShouldFailAsync("ENTITY_CODE.changed_concurrently");
    }

#endif
    private static async Task<string?> CodeOf(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString();
}
