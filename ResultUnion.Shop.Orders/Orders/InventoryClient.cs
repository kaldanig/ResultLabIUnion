using System.Net.Http.Json;
using ResultUnion.Http;

namespace ResultUnion.Shop.Orders.Orders;

/// <summary>
/// The inventory service, as the orders service sees it. ResultUnion.Http reads each answer back as a result with the
/// inventory's own error, and this adapter turns every outcome into an error of this service's catalog, so the orders
/// API never sends another service's codes to its clients:
/// <list type="bullet">
///   <item><description>not enough stock and an unknown product are the customer's problem (409, 400);</description></item>
///   <item><description>an inventory that cannot be reached, or answers 503, is worth a retry later (503);</description></item>
///   <item><description>anything else means the two services disagree: a bug, reported as unexpected (500).</description></item>
/// </list>
/// </summary>
public sealed class InventoryClient(HttpClient http)
{
    public static class Errors
    {
        public static readonly Error Unavailable =
            Error.Create("inventory.unavailable", "The inventory service cannot be reached; try again shortly.", ErrorType.Unavailable);

        public static Error UnexpectedAnswer(string code) =>
            Error.Create("inventory.unexpected_answer", $"The inventory service answered '{code}', which an order should never cause.", ErrorType.Unexpected)
                .WithArgument("code", code);
    }

    private sealed record ReserveRequest(Guid OrderId, IReadOnlyList<ReserveLine> Lines);

    private sealed record ReserveLine(string Sku, int Quantity);

    // No answer is not a refusal: the inventory may have taken the stock before the connection dropped, the timeout
    // passed or the client of this service went away. The order is not stored, and a retry is a new order, so this one's
    // stock is released first. If the inventory is still down, the release fails too and the stock stays held; a real
    // system queues it (an outbox).
    public async Task<Result> ReserveAsync(Order order, CancellationToken ct)
    {
        Result reserved;
        try
        {
            reserved = await CallAsync(() => http.PostAsJsonAsync(
                "/reservations",
                new ReserveRequest(order.Id, [.. order.Lines.Select(line => new ReserveLine(line.Product, line.Quantity))]),
                ct), ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await ReleaseUnansweredAsync(order.Id).ConfigureAwait(false);
            throw;
        }

        if (reserved.TryGetError(out var error) && error.Code == Errors.Unavailable.Code)
            await ReleaseUnansweredAsync(order.Id).ConfigureAwait(false);

        return reserved;
    }

    // A reservation that is already gone (the inventory restarted, or a retry released it) needs no release.
    public Task<Result> ReleaseAsync(Guid orderId, CancellationToken ct) =>
        CallAsync(() => http.DeleteAsync($"/reservations/{orderId}", ct), ct, alreadyDone: "reservation.not_found");

    // Not on the request's token: the client of this service leaving must not stop the release it caused. HttpClient's
    // own timeout still bounds it.
    private async Task ReleaseUnansweredAsync(Guid orderId) =>
        _ = await ReleaseAsync(orderId, CancellationToken.None).ConfigureAwait(false);

    // The one place with a hand-written catch: a refused connection and HttpClient's own timeout are the same outcome
    // as a 503, while the caller's cancellation still propagates.
    private static async Task<Result> CallAsync(Func<Task<HttpResponseMessage>> send, CancellationToken ct, string? alreadyDone = null)
    {
        Result answer;
        try
        {
            answer = await send().ReadResultAsync(ct).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            return Errors.Unavailable;
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return Errors.Unavailable;
        }

        return answer.TryGetError(out var error) && error.Code != alreadyDone ? Translate(error) : Result.Success();
    }

    private static Error Translate(Error error) => error.Code switch
    {
        "stock.insufficient" => Order.Errors.OutOfStock(Text(error, "sku"), Number(error, "requested"), Number(error, "available")),
        "stock.unknown_sku" => Order.Errors.UnknownProduct(Text(error, "sku")),
        _ when error.Type == ErrorType.Unavailable => Errors.Unavailable,
        _ => Errors.UnexpectedAnswer(error.Code),
    };

    // ResultUnion.Http reads the arguments back as plain values: whole numbers as int, text as string.
    private static string Text(Error error, string name) =>
        error.Arguments.TryGetValue(name, out var value) && value is string text ? text : "?";

    private static int Number(Error error, string name) =>
        error.Arguments.TryGetValue(name, out var value) && value is int number ? number : 0;
}
