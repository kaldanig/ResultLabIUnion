extern alias inventory;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using InventoryProgram = inventory::Program;

namespace ResultUnion.Shop.Tests;

/// <summary>The inventory service on its own: what the orders service, or any other client, receives.</summary>
public sealed class InventoryTests(WebApplicationFactory<InventoryProgram> factory) : IClassFixture<WebApplicationFactory<InventoryProgram>>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task A_reservation_takes_the_stock_and_its_release_gives_it_back()
    {
        var before = await AvailableAsync("PEAR");
        var orderId = Guid.NewGuid();

        var reserved = await _client.PostAsJsonAsync("/reservations", new { orderId, lines = new[] { new { sku = "pear", quantity = 2 }, new { sku = "PEAR", quantity = 1 } } }, Ct);
        Assert.Equal(HttpStatusCode.Created, reserved.StatusCode);
        Assert.Equal(before - 3, await AvailableAsync("PEAR"));                 // both lines of the same SKU

        var released = await _client.DeleteAsync($"/reservations/{orderId}", Ct);
        Assert.Equal(HttpStatusCode.NoContent, released.StatusCode);
        Assert.Equal(before, await AvailableAsync("PEAR"));
    }

    [Fact]
    public async Task Too_little_stock_is_409_with_what_is_there_and_takes_nothing()
    {
        var before = await AvailableAsync("PLUM");

        var response = await _client.PostAsJsonAsync("/reservations", new { orderId = Guid.NewGuid(), lines = new[] { new { sku = "PLUM", quantity = before + 1 } } }, Ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("stock.insufficient", problem.GetProperty("code").GetString());
        Assert.Equal(before, problem.GetProperty("arguments").GetProperty("available").GetInt32());
        Assert.Equal(before, await AvailableAsync("PLUM"));
    }

    [Fact]
    public async Task Every_invalid_line_is_reported_with_its_index()
    {
        var response = await _client.PostAsJsonAsync("/reservations", new { orderId = Guid.NewGuid(), lines = new[] { new { sku = "APPLE", quantity = 1 }, new { sku = "a", quantity = 0 } } }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var fields = problem.GetProperty("details").EnumerateArray().Select(d => (d.GetProperty("field").GetString(), d.GetProperty("code").GetString()));
        Assert.Equal([("lines[1].sku", "sku.invalid_format"), ("lines[1].quantity", "reservation_line.quantity_out_of_range")], fields);
    }

    [Fact]
    public async Task Releasing_twice_is_404_the_second_time()
    {
        var orderId = Guid.NewGuid();
        await _client.PostAsJsonAsync("/reservations", new { orderId, lines = new[] { new { sku = "APPLE", quantity = 1 } } }, Ct);
        await _client.DeleteAsync($"/reservations/{orderId}", Ct);

        var again = await _client.DeleteAsync($"/reservations/{orderId}", Ct);

        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
        Assert.Equal("reservation.not_found", (await again.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString());
    }

    private async Task<int> AvailableAsync(string sku) =>
        (await _client.GetFromJsonAsync<JsonElement>($"/stock/{sku}", Ct)).GetProperty("available").GetInt32();
}
