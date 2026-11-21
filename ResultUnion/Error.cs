using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json.Serialization;
using ResultUnion.Serialization;

namespace ResultUnion;

/// <summary>Describes why an operation failed: a stable code, a human-readable message and an <see cref="ErrorType"/> category.</summary>
/// <remarks>
/// <para>Instances are created through the factory methods, such as <see cref="NotFound(string)"/> or <see cref="Create(string, string, ErrorType)"/>.</para>
/// <para>
/// An error can name the input it is about (<see cref="Field"/>, set with <see cref="WithField(string)"/>) and can group
/// several errors (<see cref="Details"/>, created with <see cref="Validation(IEnumerable{Error})"/>), for example
/// every invalid field of a form.
/// </para>
/// <para>
/// An error can also carry named <see cref="Arguments"/> (set with <see cref="WithArgument(string, object)"/>), the values
/// behind its message, so a client can show a translated message for <see cref="Code"/> without parsing <see cref="Message"/>.
/// </para>
/// <para>
/// Errors compare by value: two errors are equal when their code, message, type and field are equal, their arguments
/// have the same names and equal values, and their details are equal element by element, in order.
/// </para>
/// <para>Every factory throws <see cref="ArgumentException"/> when the code or message is <see langword="null"/>, empty or white space.</para>
/// <para>
/// System.Text.Json serializes an error with <see cref="Serialization.ErrorJsonConverter"/>, for messages and events
/// between services: <c>{ "code", "message", "type", "field", "arguments", "details" }</c>.
/// </para>
/// </remarks>
[DebuggerDisplay("{ToString(),nq}")]
[JsonConverter(typeof(ErrorJsonConverter))]
public sealed record Error
{
    /// <summary>Gets the stable, machine-readable error code.</summary>
    public string Code { get; }

    /// <summary>Gets the human-readable error message.</summary>
    public string Message { get; }

    /// <summary>Gets the category of the error.</summary>
    public ErrorType Type { get; }

    /// <summary>Gets the name of the input the error is about, such as a form field or property; <see langword="null"/> when it is not about a single input.</summary>
    public string? Field { get; }

    /// <summary>Gets the errors grouped by this one; empty, never <see langword="null"/>, for a single error.</summary>
    /// <remarks>The list is read-only. Details can themselves have details.</remarks>
    public IReadOnlyList<Error> Details { get; }

    /// <summary>Gets the named values behind the message, such as <c>minimumAge</c> = 18; empty, never <see langword="null"/>, when there are none.</summary>
    /// <remarks>
    /// <para>
    /// Meant for localization: a client looks up a template for <see cref="Code"/>, for example
    /// "მომხმარებელი უნდა იყოს მინიმუმ {minimumAge} წლის", and fills it from these values.
    /// </para>
    /// <para>The dictionary is read-only. Names are compared ordinally. Keep values simple (numbers, strings, dates) so they serialize.</para>
    /// </remarks>
    public IReadOnlyDictionary<string, object?> Arguments { get; }

    private Error(
        string code,
        string message,
        ErrorType type,
        string? field = null,
        ReadOnlyCollection<Error>? details = null,
        ReadOnlyDictionary<string, object?>? arguments = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        Code = code;
        Message = message;
        Type = type;
        Field = field;
        Details = details ?? [];
        Arguments = arguments ?? ReadOnlyDictionary<string, object?>.Empty;
    }

    /// <summary>Rebuilds an error exactly as it was, for deserialization: any code, with its details and arguments.</summary>
    internal static Error Restore(
        string code,
        string message,
        ErrorType type,
        string? field,
        IReadOnlyList<Error>? details,
        IReadOnlyDictionary<string, object?>? arguments) =>
        new(
            code,
            message,
            type,
            field,
            details is { Count: > 0 } ? Array.AsReadOnly([.. details]) : null,
            arguments is { Count: > 0 } ? new ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>(arguments, StringComparer.Ordinal)) : null);

    private ReadOnlyCollection<Error> DetailsCollection => (ReadOnlyCollection<Error>)Details;

    private ReadOnlyDictionary<string, object?> ArgumentsDictionary => (ReadOnlyDictionary<string, object?>)Arguments;

    /// <summary>Returns a copy of this error that names the input it is about.</summary>
    /// <remarks>For example, <c>Error.Validation("Email is required.").WithField("email")</c>.</remarks>
    /// <exception cref="ArgumentException"><paramref name="field"/> is <see langword="null"/>, empty or white space.</exception>
    public Error WithField(string field)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(field);

        return new Error(Code, Message, Type, field, DetailsCollection, ArgumentsDictionary);
    }

    /// <summary>Returns a copy of this error with the named value <paramref name="name"/> = <paramref name="value"/> in its <see cref="Arguments"/>.</summary>
    /// <remarks>
    /// Chain calls to add several values:
    /// <c>Error.Create("user.too_young", $"…", ErrorType.BusinessRule).WithArgument("minimumAge", 18).WithArgument("age", age)</c>.
    /// A value with the same name replaces the previous one.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="name"/> is <see langword="null"/>, empty or white space.</exception>
    public Error WithArgument(string name, object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var arguments = new Dictionary<string, object?>(Arguments, StringComparer.Ordinal) { [name] = value };

        return new Error(Code, Message, Type, Field, DetailsCollection, new ReadOnlyDictionary<string, object?>(arguments));
    }

    /// <summary>Returns a copy of this error whose field path, and the field paths of all its details, start with <paramref name="prefix"/>.</summary>
    /// <remarks>
    /// Builds paths for nested inputs: <c>quantity</c> becomes <c>lines[2].quantity</c> with the prefix <c>lines[2]</c>,
    /// and an error without a field gets the prefix itself. Used by the collection overloads of <c>CombineAll</c>.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="prefix"/> is <see langword="null"/>, empty or white space.</exception>
    public Error WithFieldPrefix(string prefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);

        var details = Details.Count == 0
            ? DetailsCollection
            : Array.AsReadOnly(Details.Select(detail => detail.WithFieldPrefix(prefix)).ToArray());

        return new Error(Code, Message, Type, Field is null ? prefix : $"{prefix}.{Field}", details, ArgumentsDictionary);
    }

    /// <summary>Returns the innermost errors, in order: this error when it has no details, otherwise the innermost errors of every detail.</summary>
    /// <remarks>
    /// Groups can nest, for example a collection whose elements each report several fields. Flattening gives one entry per
    /// invalid input, each with its full field path, e.g. <c>lines[1].product</c> and <c>lines[1].quantity</c>.
    /// </remarks>
    public IEnumerable<Error> Flatten() =>
        Details.Count == 0
            ? [this]
            : Details.SelectMany(detail => detail.Flatten());

    /// <summary>Creates a validation failure that groups several errors, for example one per invalid field.</summary>
    /// <param name="details">The grouped errors, in order. At least one is required.</param>
    /// <remarks>The group has the code <c>validation.failed</c> and type <see cref="ErrorType.Validation"/>, whatever the types of its details.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="details"/> is <see langword="null"/>, or contains a <see langword="null"/> element.</exception>
    /// <exception cref="ArgumentException"><paramref name="details"/> is empty.</exception>
    public static Error Validation(IEnumerable<Error> details)
    {
        ArgumentNullException.ThrowIfNull(details);

        Error[] copy = [.. details];

        if (copy.Length == 0)
        {
            throw new ArgumentException("At least one error is required.", nameof(details));
        }

        foreach (var detail in copy)
        {
            ArgumentNullException.ThrowIfNull(detail, nameof(details));
        }

        return new Error("validation.failed", "One or more validation errors occurred.", ErrorType.Validation, details: Array.AsReadOnly(copy));
    }

    /// <inheritdoc/>
    public bool Equals(Error? other) =>
        other is not null
        && (ReferenceEquals(this, other)
            || (Code == other.Code
                && Message == other.Message
                && Type == other.Type
                && Field == other.Field
                && ArgumentsEqual(Arguments, other.Arguments)
                && Details.SequenceEqual(other.Details)));

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Code, Message, Type, Field, Details.Count, Arguments.Count);

    private static bool ArgumentsEqual(IReadOnlyDictionary<string, object?> left, IReadOnlyDictionary<string, object?> right) =>
        left.Count == right.Count
        && left.All(pair => right.TryGetValue(pair.Key, out var value) && Equals(pair.Value, value));

    /// <summary>Creates an unexpected, uncategorized failure, typically a defect.</summary>
    public static Error Unexpected(string message) =>
        new("unexpected", message, ErrorType.Unexpected);

    /// <summary>Creates a validation failure.</summary>
    public static Error Validation(string message) =>
        new("validation.failed", message, ErrorType.Validation);

    /// <summary>Creates a "resource does not exist" failure.</summary>
    public static Error NotFound(string message) =>
        new("resource.not_found", message, ErrorType.NotFound);

    /// <summary>Creates an authentication failure: the caller's identity is missing or could not be verified.</summary>
    public static Error Unauthorized(string message) =>
        new("unauthorized", message, ErrorType.Unauthorized);

    /// <summary>Creates an authorisation failure: the caller is known but not allowed to perform the operation.</summary>
    public static Error Forbidden(string message) =>
        new("forbidden", message, ErrorType.Forbidden);

    /// <summary>Creates a conflict failure: the current state of the resource, such as a uniqueness constraint or lifecycle stage, prevents the operation.</summary>
    public static Error Conflict(string message) =>
        new("conflict", message, ErrorType.Conflict);

    /// <summary>Creates an optimistic-concurrency failure: the resource changed since it was read. Callers may reload and retry.</summary>
    public static Error Concurrency(string message) =>
        new("concurrency.conflict", message, ErrorType.Concurrency);

    /// <summary>Creates a transient failure: a required dependency is unavailable or timed out. Callers may retry later.</summary>
    public static Error Unavailable(string message) =>
        new("dependency.unavailable", message, ErrorType.Unavailable);

    /// <summary>Creates a business rule violation failure.</summary>
    public static Error BusinessRule(string message) =>
        new("business_rule.violated", message, ErrorType.BusinessRule);

    /// <summary>Creates a rate-limit failure: too many requests or a quota is used up. Callers may retry later.</summary>
    /// <remarks>Add the wait as an argument when it is known, e.g. <c>.WithArgument("retryAfterSeconds", 30)</c>.</remarks>
    public static Error TooManyRequests(string message) =>
        new("rate_limit.exceeded", message, ErrorType.TooManyRequests);

    /// <summary>Creates an error with an application-specific code.</summary>
    /// <remarks>For example, <c>Create("deposit.already_closed", "Deposit is closed.", ErrorType.Conflict)</c>.</remarks>
    /// <exception cref="ArgumentException"><paramref name="code"/> or <paramref name="message"/> is <see langword="null"/>, empty or white space.</exception>
    public static Error Create(string code, string message, ErrorType type) =>
        new(code, message, type);

    /// <summary>The error carried by a <see langword="default"/>-constructed <see cref="Result"/> or <see cref="Result{T}"/>.</summary>
    public static Error DefaultResult { get; } =
        new("result.default", "The result was default-constructed and carries no error.", ErrorType.Unexpected);

    /// <inheritdoc/>
    /// <remarks>For example <c>validation.failed: Email is required. (field 'email')</c>; details follow in brackets.</remarks>
    public override string ToString()
    {
        var text = Field is null
            ? $"{Code}: {Message}"
            : $"{Code}: {Message} (field '{Field}')";

        return Details.Count == 0
            ? text
            : $"{text} [{string.Join("; ", Details)}]";
    }
}

/// <summary>The category of an <see cref="Error"/>, used to decide how a failure is handled, for example which HTTP status to return or whether to retry.</summary>
/// <remarks>
/// A category describes what the caller should <em>do</em> (fix the input, sign in, retry later, reload); the specific
/// reason is the <see cref="Error.Code"/>. <see cref="ErrorTypeExtensions.IsTransient(ErrorType)"/> tells whether a retry may succeed.
/// </remarks>
public enum ErrorType
{
    /// <summary>An unexpected or uncategorized failure, typically a defect (HTTP 500).</summary>
    Unexpected = 0,

    /// <summary>Input failed validation (HTTP 400).</summary>
    Validation = 1,

    /// <summary>The requested resource does not exist (HTTP 404).</summary>
    NotFound = 2,

    /// <summary>The caller is not authenticated: its identity is missing or could not be verified (HTTP 401).</summary>
    Unauthorized = 3,

    /// <summary>The current state of the resource prevents the operation, e.g. a duplicate or a closed aggregate (HTTP 409). Not retryable as is.</summary>
    Conflict = 4,

    /// <summary>A domain invariant or business rule was violated (HTTP 422).</summary>
    BusinessRule = 5,

    /// <summary>The caller is authenticated but not allowed to perform the operation (HTTP 403).</summary>
    Forbidden = 6,

    /// <summary>An optimistic-concurrency check failed because the resource changed since it was read (HTTP 409 or 412). Retryable after reloading.</summary>
    Concurrency = 7,

    /// <summary>A required dependency is unavailable or timed out (HTTP 503). Transient and retryable.</summary>
    Unavailable = 8,

    /// <summary>Too many requests, or a quota is used up (HTTP 429). Transient: retryable after waiting.</summary>
    TooManyRequests = 9
}