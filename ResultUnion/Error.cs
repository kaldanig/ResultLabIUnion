using System.Collections.ObjectModel;
using System.Diagnostics;

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
/// Errors compare by value: two errors are equal when their code, message, type and field are equal and their
/// details are equal element by element, in order.
/// </para>
/// <para>Every factory throws <see cref="ArgumentException"/> when the code or message is <see langword="null"/>, empty or white space.</para>
/// </remarks>
[DebuggerDisplay("{ToString(),nq}")]
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

    private Error(string code, string message, ErrorType type, string? field = null, ReadOnlyCollection<Error>? details = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        Code = code;
        Message = message;
        Type = type;
        Field = field;
        Details = details ?? ReadOnlyCollection<Error>.Empty;
    }

    /// <summary>Returns a copy of this error that names the input it is about.</summary>
    /// <remarks>For example, <c>Error.Validation("Email is required.").WithField("email")</c>.</remarks>
    /// <exception cref="ArgumentException"><paramref name="field"/> is <see langword="null"/>, empty or white space.</exception>
    public Error WithField(string field)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(field);

        return new Error(Code, Message, Type, field, (ReadOnlyCollection<Error>)Details);
    }

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
                && Details.SequenceEqual(other.Details)));

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Code, Message, Type, Field, Details.Count);

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
    Unavailable = 8
}