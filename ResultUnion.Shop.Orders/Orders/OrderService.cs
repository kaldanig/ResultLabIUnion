using Microsoft.EntityFrameworkCore;
using ResultUnion.Shop.Orders.Customers;
using ResultUnion.Shop.Orders.Data;

namespace ResultUnion.Shop.Orders.Orders;

public sealed record PlaceOrder(Guid? CustomerId, IReadOnlyList<PlaceOrderLine>? Lines);

public sealed record PlaceOrderLine(string? Product, int Quantity);

/// <summary>
/// The orders' use cases. Placing an order checks every input at once, then the customer, then reserves the stock in
/// the inventory service, and stores the order only when the stock is held.
/// </summary>
public sealed class OrderService(ShopDbContext db, CustomerService customers, InventoryClient inventory, TimeProvider clock)
{
    public Task<Result<Order>> PlaceAsync(PlaceOrder command, CancellationToken ct) =>
        Result.CombineAll(
                Result.FromNullable(command.CustomerId, Order.Errors.CustomerRequired.WithField("customerId")),
                Lines(command))
            .Map((customerId, lines) => (CustomerId: customerId, Lines: lines))
            .BindAsync(valid => customers.FindAsync(valid.CustomerId, ct)
                .MapErrorAsync(e => e.WithField("customerId"))
                .MapAsync(customer => Order.Place(customer.Id, valid.Lines, clock.GetUtcNow())))
            .EnsureAsync(order => inventory.ReserveAsync(order, ct))                      // stock first: 409, or 503
            .EnsureAsync(order => AddAsync(order, ct))       // a failure here leaves the stock held; see CancelAsync
            .MapErrorAsync(error => OnTheLine(error, command))
            .RecordAsync("orders.place");

    public Task<Result<Order>> FindAsync(Guid id, CancellationToken ct) =>
        db.Orders.FindAsync([id], ct).ToResultAsync(Order.Errors.NotFound(id));

    // The stock is released first: if the inventory cannot be reached, the order stays placed and the client can retry.
    // Between the release and the save another request may change the order; a real system keeps both in step with an
    // outbox, which this sample leaves out.
    public Task<Result<Order>> CancelAsync(Guid id, int version, CancellationToken ct) =>
        FindAsync(id, ct)
            .EnsureAsync(order => order.Version == version, Order.Errors.ChangedConcurrently(id, version))
            .EnsureAsync(order => order.Cancel())
            .EnsureAsync(order => inventory.ReleaseAsync(order.Id, ct))
            .EnsureAsync(order => SaveAsync(order, version, ct))
            .RecordAsync("orders.cancel");

    private static Result<IReadOnlyList<OrderLine>> Lines(PlaceOrder command) =>
        (command.Lines ?? [])
            .CombineAll(line => OrderLine.Create(line.Product, line.Quantity), field: "lines")
            .Ensure(lines => lines.Count > 0, Order.Errors.NoLines.WithField("lines"));

    // An unknown product is an invalid input: point it at the line that names it, as a validation error would be.
    private static Error OnTheLine(Error error, PlaceOrder command)
    {
        if (error.Code != "order.unknown_product" || !error.Arguments.TryGetValue("product", out var value) || value is not string product)
            return error;

        var index = (command.Lines ?? []).ToList().FindIndex(line => string.Equals(line.Product?.Trim(), product, StringComparison.OrdinalIgnoreCase));
        return index < 0 ? error : error.WithField($"lines[{index}].product");
    }

    private async Task<Result> AddAsync(Order order, CancellationToken ct)
    {
        db.Orders.Add(order);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    private Task<Result> SaveAsync(Order order, int version, CancellationToken ct)
    {
        db.Entry(order).Property(o => o.Version).OriginalValue = version;
        return Result.TryAsync(
            async () => { await db.SaveChangesAsync(ct); },
            (DbUpdateConcurrencyException _) => Order.Errors.ChangedConcurrently(order.Id, version));
    }
}
