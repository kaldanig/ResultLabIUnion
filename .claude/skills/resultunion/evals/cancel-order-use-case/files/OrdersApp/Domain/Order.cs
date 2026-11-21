namespace OrdersApp.Domain;

public enum OrderStatus { Pending, Paid, Shipped, Cancelled }

public sealed class Order
{
    private readonly List<OrderLine> _lines = [];

    public Order(Guid id, string customerEmail)
    {
        Id = id;
        CustomerEmail = customerEmail;
    }

    public Guid Id { get; }
    public string CustomerEmail { get; }
    public OrderStatus Status { get; private set; } = OrderStatus.Pending;
    public string? CancellationReason { get; private set; }
    public IReadOnlyList<OrderLine> Lines => _lines;

    // TODO: domain behavior (cancel, add lines, ...) goes here.

    internal void SetStatus(OrderStatus status) => Status = status;
    internal void SetCancellationReason(string reason) => CancellationReason = reason;
    internal void AddLine(OrderLine line) => _lines.Add(line);
}

public sealed record OrderLine(string Sku, int Quantity);
