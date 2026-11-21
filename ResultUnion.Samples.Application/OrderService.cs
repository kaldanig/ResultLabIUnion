using ResultUnion.Samples.Domain;
using ResultUnion.Samples.Application.Infrastructure;

namespace ResultUnion.Samples.Application;

public sealed class OrderService(InMemoryUserStore users, InMemoryOrderStore orders)
{
    /// <summary>
    /// Every invalid input is reported at once: the customer email and each line, with its index
    /// (<c>lines[1].quantity</c>). Then the aggregate is created and the customer must exist.
    /// </summary>
    public Task<Result<Order>> PlaceAsync(PlaceOrder command, CancellationToken cancellationToken) =>
        Result.CombineAll(
                Email.Create(command.CustomerEmail).MapError(error => error.WithField("customerEmail")),
                (command.Lines ?? []).CombineAll(OrderLine.Create, field: "lines"))
            .Bind(Order.Create)                                                      // (Email, IReadOnlyList<OrderLine>) -> Result<Order>
            .EnsureAsync(order => users.FindAsync(order.Customer, cancellationToken)
                .ToResultAsync(User.Errors.NotFound(order.Customer).WithField("customerEmail"))
                .BindAsync(_ => Result.Success()))
            .TapAsync(order => orders.AddAsync(order, cancellationToken));
}
