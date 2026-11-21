namespace ResultUnion.Showcase.Domain;

/// <summary>A value object for a password that meets the policy. Like <see cref="Email"/>, it can only exist in a valid state.</summary>
public sealed record Password
{
    public const int MinimumLength = 8;

    /// <summary>Every way a password can be rejected.</summary>
    public static class Errors
    {
        public static readonly Error Required =
            Error.Create("password.required", "Password is required.", ErrorType.Validation);

        public static readonly Error TooShort =
            Error.Create("password.too_short", $"Password must be at least {MinimumLength} characters.", ErrorType.Validation);
    }

    public string Value { get; }

    private Password(string value) => Value = value;

    public static Result<Password> Create(string? input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return Errors.Required;
        }

        return input.Length >= MinimumLength
            ? new Password(input)
            : Errors.TooShort;
    }

    // Never print the secret: not in logs, not in the debugger, not in Result<Password>.ToString().
    public override string ToString() => "********";
}
