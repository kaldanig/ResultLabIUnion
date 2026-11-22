using ResultUnion;

namespace TargetNamespace;

#if (IsString)
/// <summary>The VALUE_WORDS: required, trimmed, at most <see cref="MaxLength"/> characters.</summary>
public sealed record ValueName
{
    public const int MaxLength = VALUE_MAX_LENGTH;

    /// <summary>
    /// The errors this value can have. Codes are a contract with clients, who switch on them and translate them, so each
    /// one is declared once, here. The request field is not set: the caller knows what the request calls it.
    /// </summary>
    public static class Errors
    {
        public static readonly Error Required =
            Error.Create("VALUE_CODE.required", "The VALUE_WORDS is required.", ErrorType.Validation);

        public static Error TooLong(int length) =>
            Error.Create("VALUE_CODE.too_long", $"The VALUE_WORDS has {length} characters; at most {MaxLength} are allowed.", ErrorType.Validation)
                .WithArgument("length", length)
                .WithArgument("maxLength", MaxLength);
    }

    public string Value { get; }

    private ValueName(string value) => Value = value;

    public static Result<ValueName> Create(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return Errors.Required;  // an Error converts to Result<ValueName>

        var trimmed = input.Trim();
        return trimmed.Length <= MaxLength
            ? new ValueName(trimmed)  // so does a ValueName
            : Errors.TooLong(trimmed.Length);
    }

    public override string ToString() => Value;
}
#else
/// <summary>The VALUE_WORDS: required, from <see cref="Min"/> to <see cref="Max"/>.</summary>
public sealed record ValueName
{
#if (IsDecimal)
    public const decimal Min = VALUE_LOWESTm;
    public const decimal Max = VALUE_HIGHESTm;
#else
    public const int Min = VALUE_LOWEST;
    public const int Max = VALUE_HIGHEST;
#endif

    /// <summary>
    /// The errors this value can have. Codes are a contract with clients, who switch on them and translate them, so each
    /// one is declared once, here. The request field is not set: the caller knows what the request calls it.
    /// </summary>
    public static class Errors
    {
        public static readonly Error Required =
            Error.Create("VALUE_CODE.required", "The VALUE_WORDS is required.", ErrorType.Validation);

        public static Error OutOfRange(VALUE_TYPE value) =>
            Error.Create("VALUE_CODE.out_of_range", $"The VALUE_WORDS must be from {Min} to {Max}; {value} is not.", ErrorType.Validation)
                .WithArgument("value", value)
                .WithArgument("min", Min)
                .WithArgument("max", Max);
    }

    public VALUE_TYPE Value { get; }

    private ValueName(VALUE_TYPE value) => Value = value;

    public static Result<ValueName> Create(VALUE_TYPE? input) => input switch
    {
        null => Errors.Required,  // an Error converts to Result<ValueName>
        >= Min and <= Max => new ValueName(input.Value),  // so does a ValueName
        _ => Errors.OutOfRange(input.Value),
    };

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
#endif
