namespace ResultUnion.Showcase.Domain;

/// <summary>A value object that can only exist in a valid state: the factory returns a <see cref="Result{T}"/> instead of throwing.</summary>
public sealed record Email
{
    public string Value { get; }

    private Email(string value) => Value = value;

    public static Result<Email> Create(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return Error.Validation("Email is required.");          // Error -> Result<Email> (union conversion)
        }

        var trimmed = input.Trim();

        return trimmed.Contains('@') && !trimmed.StartsWith('@') && !trimmed.EndsWith('@')
            ? new Email(trimmed.ToLowerInvariant())                  // Email -> Result<Email>
            : Error.Validation($"'{trimmed}' is not a valid email.");
    }

    public override string ToString() => Value;
}
