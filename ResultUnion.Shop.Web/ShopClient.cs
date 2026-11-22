using System.Net.Http.Json;
using ResultUnion.Http;

namespace ResultUnion.Shop.Web;

public sealed record CustomerView(Guid Id, string Name, string Email, int Version);

public sealed record OrderLineView(string Product, int Quantity);

public sealed record OrderView(Guid Id, Guid CustomerId, IReadOnlyList<OrderLineView> Lines, string Status, DateTimeOffset PlacedAt, int Version);

public sealed record StockView(string Sku, int Available);

/// <summary>The language the user picked; every call sends it as Accept-Language, so the services answer in it.</summary>
public sealed class ShopLanguage
{
    public string Code { get; set; } = "en";
}

/// <summary>
/// The shop's two APIs, as the web app calls them. ResultUnion.Http reads every answer back as a result: the value, or
/// the service's error with its code, arguments and one detail per invalid field, in the language of the request. Only
/// a service that cannot be reached is translated here, into this app's own error.
/// </summary>
public sealed class ShopClient(IHttpClientFactory clients, ShopLanguage language)
{
    public const string Orders = "orders";
    public const string Inventory = "inventory";

    public static class Errors
    {
        public static Error Unreachable(string service) =>
            Error.Create("shop_web.service_unreachable", $"The {service} service cannot be reached.", ErrorType.Unavailable)
                .WithArgument("service", service);
    }

    public Task<Result<CustomerView>> CreateCustomerAsync(string? name, string? email, CancellationToken ct) =>
        SendAsync<CustomerView>(Orders, HttpMethod.Post, "/customers", new { name, email }, ct);

    public Task<Result<CustomerView>> RenameCustomerAsync(Guid id, string? name, int version, CancellationToken ct) =>
        SendAsync<CustomerView>(Orders, HttpMethod.Put, $"/customers/{id}", new { name, version }, ct);

    public Task<Result<OrderView>> PlaceOrderAsync(Guid? customerId, IEnumerable<OrderLineView> lines, CancellationToken ct) =>
        SendAsync<OrderView>(Orders, HttpMethod.Post, "/orders", new { customerId, lines }, ct);

    public Task<Result<OrderView>> CancelOrderAsync(Guid id, int version, CancellationToken ct) =>
        SendAsync<OrderView>(Orders, HttpMethod.Post, $"/orders/{id}/cancel", new { version }, ct);

    public Task<Result<StockView>> StockAsync(string sku, CancellationToken ct) =>
        SendAsync<StockView>(Inventory, HttpMethod.Get, $"/stock/{sku}", body: null, ct);

    // A refused connection and HttpClient's own timeout are the failures expected here; the caller's cancellation
    // still propagates.
    private async Task<Result<T>> SendAsync<T>(string service, HttpMethod method, string path, object? body, CancellationToken ct)
        where T : notnull
    {
        using var request = new HttpRequestMessage(method, path) { Content = body is null ? null : JsonContent.Create(body) };
        request.Headers.AcceptLanguage.ParseAdd(language.Code);
        try
        {
            return await clients.CreateClient(service).SendAsync(request, ct).ReadResultAsync<T>(ct);
        }
        catch (HttpRequestException)
        {
            return Errors.Unreachable(service);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return Errors.Unreachable(service);
        }
    }
}
