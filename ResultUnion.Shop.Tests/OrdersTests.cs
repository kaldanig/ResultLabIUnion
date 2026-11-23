using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace ResultUnion.Shop.Tests;

/// <summary>
/// The orders service with the inventory behind it. The tests share one inventory, so they compare its stock before
/// and after rather than with fixed numbers.
/// </summary>
public sealed class OrdersTests(ShopFixture shop) : IClassFixture<ShopFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Orders => shop.Orders;

    // ---- Customers ----

    [Fact]
    public async Task A_customer_is_created_and_a_second_one_with_the_same_email_is_409()
    {
        var email = $"ann-{Guid.NewGuid():N}@example.com";
        var created = await Orders.PostAsJsonAsync("/customers", new { name = "Ann", email }, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var again = await Orders.PostAsJsonAsync("/customers", new { name = "Ann again", email = email.ToUpperInvariant() }, Ct);

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("customer.email_taken", await CodeOf(again));
    }

    [Fact]
    public async Task A_rename_from_a_stale_version_is_409_and_from_the_current_one_200()
    {
        var customer = await CreateCustomerAsync();
        var version = customer.GetProperty("version").GetInt32();
        var url = $"/customers/{customer.GetProperty("id").GetGuid()}";

        var renamed = await Orders.PutAsJsonAsync(url, new { name = "Ann Smith", version }, Ct);
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        Assert.Equal(version + 1, (await renamed.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("version").GetInt32());

        var stale = await Orders.PutAsJsonAsync(url, new { name = "Ann Jones", version }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("customer.changed_concurrently", await CodeOf(stale));
    }

    [Fact]
    public async Task A_delete_needs_the_current_version()
    {
        var customer = await CreateCustomerAsync();
        var url = $"/customers/{customer.GetProperty("id").GetGuid()}";
        var version = customer.GetProperty("version").GetInt32();

        Assert.Equal(HttpStatusCode.Conflict, (await Orders.DeleteAsync($"{url}?version={version + 1}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Orders.DeleteAsync($"{url}?version={version}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Orders.GetAsync(url, Ct)).StatusCode);
    }

    // ---- Orders, across the two services ----

    [Fact]
    public async Task An_order_reserves_its_stock_and_its_cancellation_releases_it()
    {
        var customer = await CreateCustomerAsync();
        var before = await shop.AvailableAsync("APPLE", Ct);

        var placed = await Orders.PostAsJsonAsync("/orders", new { customerId = customer.GetProperty("id").GetGuid(), lines = new[] { new { product = "apple", quantity = 3 } } }, Ct);
        Assert.Equal(HttpStatusCode.Created, placed.StatusCode);
        Assert.Equal(before - 3, await shop.AvailableAsync("APPLE", Ct));

        var order = await placed.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var cancelled = await Orders.PostAsJsonAsync($"/orders/{order.GetProperty("id").GetGuid()}/cancel", new { version = order.GetProperty("version").GetInt32() }, Ct);
        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        Assert.Equal("Cancelled", (await cancelled.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("status").GetString());
        Assert.Equal(before, await shop.AvailableAsync("APPLE", Ct));
    }

    [Fact]
    public async Task The_inventorys_answer_comes_back_as_this_services_error_with_its_arguments()
    {
        var customer = await CreateCustomerAsync();
        var available = await shop.AvailableAsync("PLUM", Ct);

        var response = await PlaceAsync(customer, ("PLUM", available + 1));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("order.out_of_stock", problem.GetProperty("code").GetString());      // not the inventory's stock.insufficient
        Assert.Equal(available, problem.GetProperty("arguments").GetProperty("available").GetInt32());
        Assert.Equal(available + 1, problem.GetProperty("arguments").GetProperty("requested").GetInt32());
        Assert.Equal(available, await shop.AvailableAsync("PLUM", Ct));                      // nothing was taken
    }

    [Fact]
    public async Task An_unknown_product_is_400_on_the_line_that_names_it()
    {
        var response = await PlaceAsync(await CreateCustomerAsync(), ("APPLE", 1), ("MANGO", 1));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("order.unknown_product", problem.GetProperty("code").GetString());
        Assert.True(problem.GetProperty("errors").TryGetProperty("lines[1].product", out _));
    }

    [Fact]
    public async Task Every_invalid_input_is_reported_before_anything_is_called()
    {
        var response = await Orders.PostAsJsonAsync("/orders", new { lines = new[] { new { product = "APPLE", quantity = 0 }, new { product = "", quantity = 1 } } }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var fields = problem.GetProperty("details").EnumerateArray().Select(d => (d.GetProperty("field").GetString(), d.GetProperty("code").GetString()));
        Assert.Equal(
            [("customerId", "order.customer_required"), ("lines[0].quantity", "order_line.quantity_out_of_range"), ("lines[1].product", "order_line.product_required")],
            fields);
    }

    [Fact]
    public async Task An_inventory_that_cannot_be_reached_is_503()
    {
        var orders = shop.CreateOrders(() => new Unreachable());
        var created = await orders.PostAsJsonAsync("/customers", new { name = "Bob", email = $"bob-{Guid.NewGuid():N}@example.com" }, Ct);
        var customerId = (await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();

        var response = await orders.PostAsJsonAsync("/orders", new { customerId, lines = new[] { new { product = "APPLE", quantity = 1 } } }, Ct);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("inventory.unavailable", await CodeOf(response));
    }

    [Fact]
    public async Task A_reservation_whose_answer_is_lost_is_released_and_the_order_is_503()
    {
        var orders = shop.CreateOrders(() => new AnswerLost(shop.InventoryHandler()));
        var created = await orders.PostAsJsonAsync("/customers", new { name = "Eve", email = $"eve-{Guid.NewGuid():N}@example.com" }, Ct);
        var customerId = (await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
        var before = await shop.AvailableAsync("PEAR", Ct);

        var response = await orders.PostAsJsonAsync("/orders", new { customerId, lines = new[] { new { product = "PEAR", quantity = 2 } } }, Ct);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("inventory.unavailable", await CodeOf(response));
        Assert.Equal(before, await shop.AvailableAsync("PEAR", Ct));
    }

    [Fact]
    public async Task A_second_cancellation_is_422_and_a_stale_version_409()
    {
        var placed = await (await PlaceAsync(await CreateCustomerAsync(), ("APPLE", 1))).Content.ReadFromJsonAsync<JsonElement>(Ct);
        var url = $"/orders/{placed.GetProperty("id").GetGuid()}/cancel";
        var version = placed.GetProperty("version").GetInt32();

        var cancelled = await Orders.PostAsJsonAsync(url, new { version }, Ct);
        var stale = await Orders.PostAsJsonAsync(url, new { version }, Ct);
        var again = await Orders.PostAsJsonAsync(url, new { version = version + 1 }, Ct);

        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        Assert.Equal("order.changed_concurrently", await CodeOf(stale));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, again.StatusCode);
        Assert.Equal("order.already_cancelled", await CodeOf(again));
    }

    [Fact]
    public async Task With_Accept_Language_ka_the_messages_are_Georgian_and_the_codes_the_same()
    {
        var customer = await CreateCustomerAsync();
        var available = await shop.AvailableAsync("PLUM", Ct);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/orders")
        {
            Content = JsonContent.Create(new { customerId = customer.GetProperty("id").GetGuid(), lines = new[] { new { product = "PLUM", quantity = available + 1 } } }),
        };
        request.Headers.AcceptLanguage.ParseAdd("ka");

        var problem = await (await Orders.SendAsync(request, Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct);

        Assert.Equal("order.out_of_stock", problem.GetProperty("code").GetString());
        Assert.Equal($"მარაგში მხოლოდ {available} ცალი PLUM-ია; შეკვეთას {available + 1} სჭირდება.", problem.GetProperty("detail").GetString());
    }

    private async Task<JsonElement> CreateCustomerAsync()
    {
        var response = await Orders.PostAsJsonAsync("/customers", new { name = "Ann", email = $"ann-{Guid.NewGuid():N}@example.com" }, Ct);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private Task<HttpResponseMessage> PlaceAsync(JsonElement customer, params (string Product, int Quantity)[] lines) =>
        Orders.PostAsJsonAsync("/orders", new { customerId = customer.GetProperty("id").GetGuid(), lines = lines.Select(l => new { product = l.Product, quantity = l.Quantity }) }, Ct);

    private static async Task<string?> CodeOf(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString();

    // A refused connection, as HttpClient reports one.
    private sealed class Unreachable : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("Connection refused (inventory)");
    }

    // The inventory reserves the stock, but its answer never arrives: the client sees HttpClient's timeout.
    private sealed class AnswerLost(HttpMessageHandler inventory) : DelegatingHandler(inventory)
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = await base.SendAsync(request, cancellationToken);
            if (request.Method != HttpMethod.Post)
                return response;

            response.Dispose();
            throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout.");
        }
    }
}
