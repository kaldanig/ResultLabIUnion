// Support types the articles assume. The article code itself is in the other files, copied verbatim.
using ResultUnion;

namespace ResultUnion.Articles;

// ---- getting-started.md, section 1 (verbatim) ----
public sealed record Email
{
    public static class Errors
    {
        public static readonly Error Required =
            Error.Create("email.required", "Email is required.", ErrorType.Validation);

        public static Error InvalidFormat(string value) =>
            Error.Create("email.invalid_format", $"'{value}' is not a valid email.", ErrorType.Validation)
                .WithArgument("value", value);
    }

    public string Value { get; }

    private Email(string value) => Value = value;

    public static Result<Email> Create(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return Errors.Required;

        var trimmed = input.Trim();
        return trimmed.Contains('@')
            ? new Email(trimmed.ToLowerInvariant())
            : Errors.InvalidFormat(trimmed);
    }
}

public sealed record Password
{
    public static class Errors
    {
        public static readonly Error Required = Error.Create("password.required", "Password is required.", ErrorType.Validation);

        public static Error TooShort(int minimumLength) =>
            Error.Create("password.too_short", $"Password must be at least {minimumLength} characters.", ErrorType.Validation)
                .WithArgument("minimumLength", minimumLength);
    }

    public string Value { get; }
    private Password(string value) => Value = value;

    public static Result<Password> Create(string? input) =>
        string.IsNullOrEmpty(input) ? Errors.Required
        : input.Length < 8 ? Errors.TooShort(8)
        : new Password(input);
}

public sealed class User
{
    public static class Errors
    {
        public static readonly Error TooYoung = Error.Create("user.too_young", "Users must be at least 18.", ErrorType.BusinessRule);
        public static readonly Error InvalidAge = Error.Create("user.invalid_age", "Age must be a whole number.", ErrorType.Validation);

        public static Error EmailTaken(Email email) => EmailTaken(email.Value);

        public static Error EmailTaken(string email) =>
            Error.Create("user.email_taken", $"{email} is already registered.", ErrorType.Conflict).WithArgument("email", email);
    }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Email Email { get; private set; } = null!;
    public int Age { get; private set; }

    public static Result<int> ParseAge(string? input) =>
        int.TryParse(input, out var age) ? age : Errors.InvalidAge;

    public static Result<User> Create(Email email, Password password, int age) =>
        new User { Email = email, Age = age };
}

public sealed record UserResponse(Guid Id, string Email)
{
    public static UserResponse From(User user) => new(user.Id, user.Email.Value);
}

public sealed record OrderLineInput(string? Product, int Quantity);

public sealed record OrderLine(string Product, int Quantity)
{
    public static class Errors
    {
        public static readonly Error ProductRequired =
            Error.Create("order_line.product_required", "Product is required.", ErrorType.Validation);
    }

    public static Result<OrderLine> Create(string? product, int quantity) =>
        string.IsNullOrEmpty(product)
            ? Errors.ProductRequired.WithField("product")
            : new OrderLine(product, quantity);
}

public sealed record CancellationReason(string Value)
{
    public static class Errors
    {
        public static readonly Error Required =
            Error.Create("cancellation_reason.required", "A reason is required.", ErrorType.Validation);
    }

    public static Result<CancellationReason> Create(string? input) =>
        string.IsNullOrWhiteSpace(input) ? Errors.Required : new CancellationReason(input);
}

public sealed class Order
{
    public static class Errors
    {
        public static Error NotFound(Guid id) =>
            Error.Create("order.not_found", $"Order {id} does not exist.", ErrorType.NotFound).WithArgument("orderId", id);

        public static Error NumberNotFound(string number) =>
            Error.Create("order.number_not_found", $"Order {number} does not exist.", ErrorType.NotFound);

        public static Error ChangedConcurrently(Guid id) =>
            Error.Create("order.changed_concurrently", "The order was changed by someone else.", ErrorType.Concurrency);

        public static readonly Error AlreadyShipped =
            Error.Create("order.already_shipped", "A shipped order cannot be cancelled.", ErrorType.BusinessRule);
    }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public string Number { get; private set; } = "";
    public Guid CustomerId { get; private set; }
    public List<OrderLine> Lines { get; private set; } = [];
    public bool Shipped { get; private set; }

    public static Result<Order> Create(Guid customerId, IReadOnlyList<OrderLine> lines) =>
        new Order { CustomerId = customerId, Lines = [.. lines] };

    public Result<Order> Cancel(CancellationReason reason) => Shipped ? Errors.AlreadyShipped : this;
}
