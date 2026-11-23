extern alias web;

using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using ErrorView = web::ResultUnion.Shop.Web.ErrorView;
using OrderLineView = web::ResultUnion.Shop.Web.OrderLineView;
using ShopClient = web::ResultUnion.Shop.Web.ShopClient;
using ShopLanguage = web::ResultUnion.Shop.Web.ShopLanguage;
using WebProgram = web::Program;

namespace ResultUnion.Shop.Tests;

/// <summary>
/// The web app's client against the two services in memory: what the page shows comes from these errors, read back by
/// ResultUnion.Http from the services' problem responses.
/// </summary>
public sealed class WebTests(ShopFixture shop) : IClassFixture<ShopFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Every_invalid_input_lands_on_its_field_in_the_chosen_language()
    {
        var client = Client(language: "ka");

        var result = await client.PlaceOrderAsync(null, [new OrderLineView("APPLE", 0)], Ct);

        Assert.True(result.TryGetError(out var error));
        var view = new ErrorView(error, "ka");
        Assert.True(view.AllOnFields);                                          // no line of its own on the form
        Assert.Equal(["შეკვეთას მომხმარებელი სჭირდება."], view.For("customerId"));
        Assert.Equal(["რაოდენობა 1-დან 100-მდე უნდა იყოს; 0 ასე არ არის."], view.For("lines[0].quantity"));
    }

    [Fact]
    public async Task An_order_for_more_than_is_in_stock_is_shown_with_its_message_and_code()
    {
        var client = Client();
        var customer = (await client.CreateCustomerAsync("Ann", $"ann-{Guid.NewGuid():N}@example.com", Ct)).ResultValue;
        var available = (await client.StockAsync("PLUM", Ct)).ResultValue.Available;

        var result = await client.PlaceOrderAsync(customer.Id, [new OrderLineView("PLUM", available + 1)], Ct);

        Assert.True(result.TryGetError(out var error));
        var view = new ErrorView(error, "en");
        Assert.False(view.AllOnFields);
        Assert.Equal("order.out_of_stock", view.Code);
        Assert.Equal($"Only {available} of PLUM are in stock; the order needs {available + 1}.", view.Message);
    }

    [Fact]
    public async Task A_rename_from_a_stale_version_says_to_reload()
    {
        var client = Client();
        var customer = (await client.CreateCustomerAsync("Ann", $"ann-{Guid.NewGuid():N}@example.com", Ct)).ResultValue;
        Assert.True((await client.RenameCustomerAsync(customer.Id, "Ann Smith", customer.Version, Ct)).IsSuccess);

        var stale = await client.RenameCustomerAsync(customer.Id, "Ann Jones", customer.Version, Ct);

        Assert.True(stale.TryGetError(out var error));
        Assert.Equal("customer.changed_concurrently", error.Code);
        Assert.Equal("Someone changed it since you read it: reload, then try again.", new ErrorView(error, "en").Hint);
    }

    [Fact]
    public async Task A_service_that_cannot_be_reached_is_this_apps_own_transient_error()
    {
        var client = Client(orders: () => new Unreachable());

        var result = await client.CreateCustomerAsync("Ann", "ann@example.com", Ct);

        Assert.True(result.TryGetError(out var error));
        var view = new ErrorView(error, "en");
        Assert.Equal("shop_web.service_unreachable", view.Code);
        Assert.True(view.Transient);
        Assert.Equal("Try again in a moment.", view.Hint);
    }

    // The services translate their messages; the app's own error and hints follow the chosen language too.
    [Fact]
    public async Task The_apps_own_error_and_hint_are_in_the_chosen_language()
    {
        var client = Client(language: "ka", orders: () => new Unreachable());

        var result = await client.CreateCustomerAsync("Ann", "ann@example.com", Ct);

        Assert.True(result.TryGetError(out var error));
        var view = new ErrorView(error, "ka");
        Assert.Equal("shop_web.service_unreachable", view.Code);
        Assert.Equal("შეკვეთების სერვისი მიუწვდომელია.", view.Message);
        Assert.Equal("ცოტა ხანში სცადეთ.", view.Hint);
    }

    // Prerendering calls no service: the stock is loaded once the page is interactive.
    [Fact]
    public async Task The_page_renders_without_calling_the_services()
    {
        await using var factory = new WebApplicationFactory<WebProgram>();

        var response = await factory.CreateClient().GetAsync("/", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("ResultUnion shop", await response.Content.ReadAsStringAsync(Ct));
    }

    private ShopClient Client(string language = "en", Func<HttpMessageHandler>? orders = null)
    {
        var services = new ServiceCollection();
        services.AddHttpClient(ShopClient.Orders, http => http.BaseAddress = new Uri("http://orders"))
            .ConfigurePrimaryHttpMessageHandler(orders ?? shop.OrdersHandler);
        services.AddHttpClient(ShopClient.Inventory, http => http.BaseAddress = new Uri("http://inventory"))
            .ConfigurePrimaryHttpMessageHandler(shop.InventoryHandler);
        var provider = services.BuildServiceProvider();
        return new ShopClient(provider.GetRequiredService<IHttpClientFactory>(), new ShopLanguage { Code = language });
    }

    private sealed class Unreachable : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("Connection refused (orders)");
    }
}
