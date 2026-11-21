using OrdersApp.Domain;

namespace OrdersApp.Infrastructure;

/// <summary>Thrown by SaveChangesAsync when another request changed the same order first.</summary>
public sealed class OrderConcurrencyException(Guid orderId)
    : Exception($"Order {orderId} was changed by another request.")
{
    public Guid OrderId { get; } = orderId;
}

public interface IOrderRepository
{
    /// <summary>Returns null when there is no such order.</summary>
    Task<Order?> FindAsync(Guid id, CancellationToken ct);

    /// <summary>May throw <see cref="OrderConcurrencyException"/>.</summary>
    Task SaveChangesAsync(CancellationToken ct);
}

public sealed class InMemoryOrderRepository : IOrderRepository
{
    private readonly Dictionary<Guid, Order> _orders = new();

    public Task<Order?> FindAsync(Guid id, CancellationToken ct) =>
        Task.FromResult(_orders.GetValueOrDefault(id));

    public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
}
