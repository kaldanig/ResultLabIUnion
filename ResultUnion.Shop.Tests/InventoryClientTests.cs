extern alias inventory;
extern alias orders;

using System.Diagnostics;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using InventoryClient = orders::ResultUnion.Shop.Orders.Orders.InventoryClient;
using InventoryProgram = inventory::Program;
using Order = orders::ResultUnion.Shop.Orders.Orders.Order;
using OrderLine = orders::ResultUnion.Shop.Orders.Orders.OrderLine;

namespace ResultUnion.Shop.Tests;

/// <summary>
/// The orders service's inventory client on its own, against the inventory in memory. The end-to-end tests cannot cancel
/// a request on the server the moment the client gives up (the test server does not abort it), so the cancellation of
/// a reservation is tested here, on the token the endpoint would pass.
/// </summary>
public sealed class InventoryClientTests(WebApplicationFactory<InventoryProgram> inventory) : IClassFixture<WebApplicationFactory<InventoryProgram>>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // The client of the orders service gives up while the inventory's answer is on its way. The request's token is
    // cancelled; the release must not be, or the stock stays held for an order that was never stored.
    [Fact]
    public async Task A_reservation_cancelled_after_the_inventory_took_the_stock_is_released()
    {
        var reserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = new HttpClient(new AnswerHeld(inventory.Server.CreateHandler(), reserved)) { BaseAddress = new Uri("http://inventory") };
        var client = new InventoryClient(http);
        var before = await AvailableAsync("PEAR");
        var order = Order.Place(Guid.NewGuid(), [OrderLine.Create("PEAR", 2).ResultValue], DateTimeOffset.UtcNow);

        using var request = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var reserving = client.ReserveAsync(order, request.Token);
        await reserved.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        Assert.Equal(before - 2, await AvailableAsync("PEAR"));                 // the inventory holds the stock

        await request.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reserving);
        Assert.Equal(before, await AvailableAsync("PEAR"));                     // released before the cancellation surfaced
    }

    private async Task<int> AvailableAsync(string sku) =>
        (await inventory.CreateClient().GetFromJsonAsync<System.Text.Json.JsonElement>($"/stock/{sku}", Ct)).GetProperty("available").GetInt32();

    // The inventory reserves the stock, and its answer is held until the request is cancelled. A request on a token
    // that is already cancelled is not sent, as with a real connection (the in-memory test server would still run it).
    private sealed class AnswerHeld(HttpMessageHandler inventory, TaskCompletionSource reserved) : DelegatingHandler(inventory)
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var response = await base.SendAsync(request, cancellationToken);
            if (request.Method != HttpMethod.Post)
                return response;

            reserved.TrySetResult();
            using (response)
                await Task.Delay(Timeout.Infinite, cancellationToken);

            throw new UnreachableException();
        }
    }
}
