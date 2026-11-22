extern alias inventory;
extern alias orders;

using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using InventoryClient = orders::ResultUnion.Shop.Orders.Orders.InventoryClient;
using InventoryProgram = inventory::Program;
using OrdersProgram = orders::Program;

namespace ResultUnion.Shop.Tests;

/// <summary>
/// The two services in memory: the orders service with a database file of its own, and its inventory client sent to the
/// inventory's test server instead of the network.
/// </summary>
public sealed class ShopFixture : IAsyncDisposable
{
    private readonly WebApplicationFactory<InventoryProgram> _inventory = new();
    private readonly List<IAsyncDisposable> _orders = [];

    public ShopFixture()
    {
        Inventory = _inventory.CreateClient();
        Orders = CreateOrders(() => _inventory.Server.CreateHandler(), out var orders);
        OrdersHandler = orders.Server.CreateHandler;
        InventoryHandler = _inventory.Server.CreateHandler;
    }

    /// <summary>Handlers to the two test servers, for a client of the shop (the web app's <c>ShopClient</c>).</summary>
    public Func<HttpMessageHandler> OrdersHandler { get; }

    public Func<HttpMessageHandler> InventoryHandler { get; }

    public HttpClient Inventory { get; }

    public HttpClient Orders { get; }

    /// <summary>An orders service whose calls to the inventory go through <paramref name="handler"/>.</summary>
    public HttpClient CreateOrders(Func<HttpMessageHandler> handler) => CreateOrders(handler, out _);

    private HttpClient CreateOrders(Func<HttpMessageHandler> handler, out WebApplicationFactory<OrdersProgram> created)
    {
        var database = Path.Combine(Path.GetTempPath(), $"shop-{Guid.NewGuid():N}.db");
        var factory = new WebApplicationFactory<OrdersProgram>().WithWebHostBuilder(host => host
            .UseSetting("ConnectionStrings:Shop", $"Data Source={database}")
            .UseSetting("Inventory:BaseAddress", "http://inventory")
            .ConfigureServices(services => services.AddHttpClient<InventoryClient>().ConfigurePrimaryHttpMessageHandler(handler)));
        _orders.Add(factory);
        created = factory;
        return factory.CreateClient();
    }

    public async Task<int> AvailableAsync(string sku, CancellationToken ct) =>
        (await Inventory.GetFromJsonAsync<JsonElement>($"/stock/{sku}", ct)).GetProperty("available").GetInt32();

    public async ValueTask DisposeAsync()
    {
        foreach (var factory in _orders)
            await factory.DisposeAsync();

        await _inventory.DisposeAsync();
    }
}
