namespace ResultUnion.Showcase.Domain;

/// <summary>A value object that can only exist in a valid state: the factory returns a <see cref="Result{T}"/> instead of throwing.</summary>
public sealed record Email
{
    /// <summary>Every way an email can be rejected. The codes are a contract: clients may switch on them.</summary>
    public static class Errors
    {
        public static readonly Error Required =
            Error.Create("email.required", "Email is required.", ErrorType.Validation);

        public static Error InvalidFormat(string value) =>
            Error.Create("email.invalid_format", $"'{value}' is not a valid email.", ErrorType.Validation);
    }

    public string Value { get; }

    private Email(string value) => Value = value;

    public static Result<Email> Create(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return Errors.Required;                                  // Error -> Result<Email> (union conversion)
        }

        var trimmed = input.Trim();

        return trimmed.Contains('@') && !trimmed.StartsWith('@') && !trimmed.EndsWith('@')
            ? new Email(trimmed.ToLowerInvariant())                  // Email -> Result<Email>
            : Errors.InvalidFormat(trimmed);
    }

    public override string ToString() => Value;
}
