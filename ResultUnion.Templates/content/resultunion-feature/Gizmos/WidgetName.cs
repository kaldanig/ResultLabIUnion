using ResultUnion;

namespace TargetNamespace.Gizmos;

/// <summary>The name of a ENTITY_WORDS: required, trimmed, at most <see cref="MaxLength"/> characters.</summary>
public sealed record WidgetName
{
    public const int MaxLength = 100;

    /// <summary>
    /// The errors a name can have. Codes are a contract with clients, who switch on them and translate them, so each
    /// one is declared once, here. The request field is not set: the caller knows what the request calls it.
    /// </summary>
    public static class Errors
    {
        public static readonly Error Required =
            Error.Create("ENTITY_CODE_name.required", "The ENTITY_WORDS name is required.", ErrorType.Validation);

        public static Error TooLong(int length) =>
            Error.Create("ENTITY_CODE_name.too_long", $"The ENTITY_WORDS name has {length} characters; at most {MaxLength} are allowed.", ErrorType.Validation)
                .WithArgument("length", length)
                .WithArgument("maxLength", MaxLength);
    }

    public string Value { get; }

    private WidgetName(string value) => Value = value;

    public static Result<WidgetName> Create(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return Errors.Required;  // an Error converts to Result<WidgetName>

        var trimmed = input.Trim();
        return trimmed.Length <= MaxLength
            ? new WidgetName(trimmed)  // so does a WidgetName
            : Errors.TooLong(trimmed.Length);
    }

    public override string ToString() => Value;
}
