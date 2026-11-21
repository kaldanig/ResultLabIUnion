using ResultUnion.Samples.Domain;

namespace ResultUnion.WebApi;

// The HTTP contract. Requests carry raw input (strings, so invalid values reach the domain and get its error codes);
// responses are DTOs mapped from domain objects. Results themselves are never serialized.

/// <summary>Registration form. <c>Age</c> is a string on purpose, so "abc" produces the domain error <c>user.age_not_a_number</c>.</summary>
public sealed record RegisterUserRequest(string? Email, string? Password, string? Age);

public sealed record UserResponse(Guid Id, string Email, int Age, bool IsActive)
{
    public static UserResponse From(User user) => new(user.Id, user.Email.Value, user.Age, user.IsActive);
}

/// <summary>An order with its lines; each line is validated by <see cref="OrderLine.Create"/>.</summary>
public sealed record PlaceOrderRequest(string? CustomerEmail, OrderLineInput[]? Lines);

public sealed record OrderLineResponse(string Product, int Quantity);

public sealed record OrderResponse(Guid Id, string Customer, IReadOnlyList<OrderLineResponse> Lines, int TotalQuantity)
{
    public static OrderResponse From(Order order) => new(
        order.Id,
        order.Customer.Value,
        [.. order.Lines.Select(line => new OrderLineResponse(line.Product, line.Quantity))],
        order.TotalQuantity);
}
