namespace ResultUnion.Shop.Orders.Customers;

/// <summary>A customer's name: required, trimmed, at most <see cref="MaxLength"/> characters.</summary>
public sealed record CustomerName
{
    public const int MaxLength = 100;

    public static class Errors
    {
        public static readonly Error Required =
            Error.Create("customer_name.required", "The customer's name is required.", ErrorType.Validation);

        public static Error TooLong(int length) =>
            Error.Create("customer_name.too_long", $"The name has {length} characters; at most {MaxLength} are allowed.", ErrorType.Validation)
                .WithArgument("length", length)
                .WithArgument("maxLength", MaxLength);
    }

    public string Value { get; }

    private CustomerName(string value) => Value = value;

    public static Result<CustomerName> Create(string? input) =>
        input?.Trim() switch
        {
            null or "" => Errors.Required,
            { Length: > MaxLength } tooLong => Errors.TooLong(tooLong.Length),
            var name => new CustomerName(name),
        };

    public override string ToString() => Value;
}

/// <summary>A customer's email: required, with an @, stored in lower case.</summary>
public sealed record CustomerEmail
{
    public static class Errors
    {
        public static readonly Error Required =
            Error.Create("customer_email.required", "The email is required.", ErrorType.Validation);

        public static Error Invalid(string value) =>
            Error.Create("customer_email.invalid", $"'{value}' is not an email address.", ErrorType.Validation)
                .WithArgument("value", value);
    }

    public string Value { get; }

    private CustomerEmail(string value) => Value = value;

    public static Result<CustomerEmail> Create(string? input)
    {
        var trimmed = input?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return Errors.Required;

        var at = trimmed.IndexOf('@');
        return at > 0 && at < trimmed.Length - 1 ? new CustomerEmail(trimmed.ToLowerInvariant()) : Errors.Invalid(trimmed);
    }

    public override string ToString() => Value;
}

/// <summary>
/// A customer. <see cref="Version"/> is the concurrency token: a client sends the version it read, and the database
/// refuses a change made from an older one.
/// </summary>
public sealed class Customer
{
    public static class Errors
    {
        public static Error NotFound(Guid id) =>
            Error.Create("customer.not_found", $"There is no customer {id}.", ErrorType.NotFound)
                .WithArgument("id", id);

        public static Error EmailTaken(CustomerEmail email) =>
            Error.Create("customer.email_taken", $"A customer with the email {email} already exists.", ErrorType.Conflict)
                .WithArgument("email", email.Value);

        public static Error ChangedConcurrently(Guid id, int version) =>
            Error.Create("customer.changed_concurrently", $"Customer {id} changed after version {version} was read.", ErrorType.Concurrency)
                .WithArgument("id", id)
                .WithArgument("version", version);
    }

    public Guid Id { get; private set; }
    public CustomerName Name { get; private set; } = null!;
    public CustomerEmail Email { get; private set; } = null!;
    public int Version { get; private set; }

    private Customer()
    {
    }

    public static Customer Create(CustomerName name, CustomerEmail email) =>
        new() { Id = Guid.NewGuid(), Name = name, Email = email, Version = 1 };

    public void Rename(CustomerName name)
    {
        Name = name;
        Version++;
    }
}
