using System.Text.Json;
using FluentValidation;
using FluentValidation.Results;

namespace ResultUnion.FluentValidation;

/// <summary>
/// Turns FluentValidation's <see cref="ValidationResult"/> into a <see cref="Result"/>, and validates straight into a
/// <see cref="Result{T}"/>.
/// </summary>
/// <remarks>
/// <para>
/// Each <see cref="ValidationFailure"/> becomes a <see cref="ErrorType.Validation"/> error with the failure's
/// <see cref="ValidationFailure.ErrorCode"/> as its code, its message, the property path as its
/// <see cref="Error.Field"/>, and the rule's numeric, string and Boolean placeholder values (such as <c>MinLength</c>) as
/// its arguments. One failure is returned as it is; several are grouped in a <c>validation.failed</c> error, exactly as
/// <c>Result.CombineAll</c> does, so the HTTP and gRPC integrations report one field error
/// each.
/// </para>
/// <para>
/// Give rules stable codes with <c>WithErrorCode("email.required")</c>: clients switch on the code, and FluentValidation's
/// default codes are validator names (<c>NotEmptyValidator</c>). The attempted value (<see cref="ValidationFailure.AttemptedValue"/>)
/// is never copied, because it may be a password or other personal data. Every failure fails the result, whatever its
/// <see cref="Severity"/>, as <see cref="ValidationResult.IsValid"/> does.
/// </para>
/// </remarks>
public static class ValidationResultExtensions
{
    // Placeholders that describe the property rather than the rule, or that may hold the user's input.
    private static readonly HashSet<string> SkippedPlaceholders =
        ["PropertyName", "PropertyPath", "PropertyValue", "CollectionIndex"];

    /// <param name="result">The outcome of a FluentValidation validator.</param>
    extension(ValidationResult result)
    {
        /// <summary>
        /// Returns a success when the validation passed, or a failure with one field error per validation failure; field
        /// names are camel case (<c>Lines[1].Quantity</c> becomes <c>lines[1].quantity</c>), as ASP.NET Core writes JSON.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="result"/> is <see langword="null"/>.</exception>
        public Result ToResult() => result.ToResult(CamelCaseField);

        /// <summary>Returns a success when the validation passed, or a failure with one field error per validation failure, named by <paramref name="fieldName"/>.</summary>
        /// <param name="fieldName">Turns FluentValidation's property path into the field name a client sees; it is not called for a failure without a property.</param>
        /// <exception cref="ArgumentNullException"><paramref name="result"/> or <paramref name="fieldName"/> is <see langword="null"/>.</exception>
        public Result ToResult(Func<string, string> fieldName)
        {
            ArgumentNullException.ThrowIfNull(result);
            ArgumentNullException.ThrowIfNull(fieldName);

            return result.Errors switch
            {
                [] => Result.Success(),
                [var single] => ToError(single, fieldName),
                var failures => Error.Validation(failures.Select(failure => ToError(failure, fieldName))),
            };
        }
    }

    /// <typeparam name="T">The type of the validated value.</typeparam>
    /// <param name="validator">The validator to run.</param>
    extension<T>(IValidator<T> validator)
        where T : notnull
    {
        /// <summary>Validates <paramref name="instance"/> and returns it as a success, or a failure with one field error per validation failure.</summary>
        /// <remarks>
        /// For a validator with asynchronous rules use <c>ValidateToResultAsync</c>; FluentValidation throws when such a
        /// validator is run synchronously.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="validator"/> or <paramref name="instance"/> is <see langword="null"/>.</exception>
        public Result<T> ValidateToResult(T instance)
        {
            ArgumentNullException.ThrowIfNull(validator);
            ArgumentNullException.ThrowIfNull(instance);

            return validator.Validate(instance).ToResult().Bind(() => Result<T>.Success(instance));
        }

        /// <summary>Validates <paramref name="instance"/>, including asynchronous rules, and returns it as a success, or a failure with one field error per validation failure.</summary>
        /// <remarks>Cancellation is not turned into a failure: an <see cref="OperationCanceledException"/> surfaces through the returned task.</remarks>
        /// <exception cref="ArgumentNullException"><paramref name="validator"/> or <paramref name="instance"/> is <see langword="null"/>.</exception>
        public Task<Result<T>> ValidateToResultAsync(T instance, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(validator);
            ArgumentNullException.ThrowIfNull(instance);

            return Core(validator, instance, cancellationToken);

            static async Task<Result<T>> Core(IValidator<T> validator, T instance, CancellationToken cancellationToken) =>
                (await validator.ValidateAsync(instance, cancellationToken).ConfigureAwait(false)).ToResult().Bind(() => Result<T>.Success(instance));
        }
    }

    private static Error ToError(ValidationFailure failure, Func<string, string> fieldName)
    {
        var code = string.IsNullOrWhiteSpace(failure.ErrorCode) ? "validation.failed" : failure.ErrorCode;
        var message = string.IsNullOrWhiteSpace(failure.ErrorMessage) ? code : failure.ErrorMessage;
        var error = Error.Create(code, message, ErrorType.Validation);

        if (!string.IsNullOrWhiteSpace(failure.PropertyName))
            error = error.WithField(fieldName(failure.PropertyName));

        if (failure.FormattedMessagePlaceholderValues is { } placeholders)
        {
            foreach (var (name, value) in placeholders.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                if (!SkippedPlaceholders.Contains(name) && IsSimple(value))
                    error = error.WithArgument(JsonNamingPolicy.CamelCase.ConvertName(name), value);
            }
        }

        return error;
    }

    // Numbers, strings and Booleans survive JSON unchanged (ErrorJsonConverter); anything else would come back different.
    private static bool IsSimple(object? value) =>
        value is string or bool or int or long or short or byte or sbyte or uint or ulong or ushort or decimal or double or float;

    // "Lines[1].Quantity" → "lines[1].quantity": each segment's name in camel case, indexes kept.
    private static string CamelCaseField(string propertyPath) =>
        string.Join('.', propertyPath.Split('.').Select(segment =>
        {
            var bracket = segment.IndexOf('[', StringComparison.Ordinal);
            return bracket < 0
                ? JsonNamingPolicy.CamelCase.ConvertName(segment)
                : JsonNamingPolicy.CamelCase.ConvertName(segment[..bracket]) + segment[bracket..];
        }));
}
