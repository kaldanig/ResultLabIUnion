using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ResultUnion.AspNetCore;

/// <summary>One entry of the <c>details</c> extension of a problem response: the machine-readable part of a grouped error.</summary>
/// <param name="Code">The error code, e.g. <c>email.invalid_format</c>.</param>
/// <param name="Field">The input the error is about, or <see langword="null"/>.</param>
/// <param name="Message">The human-readable message.</param>
public sealed record ProblemErrorDetail(string Code, string? Field, string Message);

/// <summary>Maps <see cref="Error"/> and <see cref="ErrorType"/> to HTTP: status codes and RFC 9457 problem details.</summary>
public static class ProblemDetailsMapping
{
    /// <summary>The <see cref="ProblemDetails.Detail"/> used for <see cref="ErrorType.Unexpected"/>, whose own message may expose internals.</summary>
    public const string UnexpectedDetail = "An unexpected error occurred.";

    /// <param name="type">The error category.</param>
    extension(ErrorType type)
    {
        /// <summary>Gets the HTTP status code for the category.</summary>
        /// <remarks>
        /// Validation 400, Unauthorized 401, Forbidden 403, NotFound 404, Conflict and Concurrency 409, BusinessRule 422,
        /// Unavailable 503, Unexpected (and any unknown value) 500.
        /// </remarks>
        public int ToStatusCode() => type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict or ErrorType.Concurrency => StatusCodes.Status409Conflict,
            ErrorType.BusinessRule => StatusCodes.Status422UnprocessableEntity,
            ErrorType.Unavailable => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status500InternalServerError,
        };
    }

    /// <param name="error">The error to describe.</param>
    extension(Error error)
    {
        /// <summary>Creates a problem details object for the error.</summary>
        /// <remarks>
        /// <para>
        /// <see cref="ProblemDetails.Status"/> comes from <see cref="Error.Type"/>, <see cref="ProblemDetails.Detail"/> from
        /// <see cref="Error.Message"/>, and the stable <see cref="Error.Code"/> is added as the <c>code</c> extension.
        /// <see cref="ProblemDetails.Title"/> and <see cref="ProblemDetails.Type"/> are left to ASP.NET Core's defaults.
        /// </para>
        /// <para>
        /// An error with a <see cref="Error.Field"/> or <see cref="Error.Details"/> becomes an
        /// <see cref="HttpValidationProblemDetails"/>: <c>errors</c> holds the messages per field (<c>""</c> for errors
        /// without a field), and the <c>details</c> extension holds every <see cref="ProblemErrorDetail"/> with its code.
        /// Nested details, such as a collection element that has several invalid fields, are flattened: only the
        /// innermost errors are listed, each with its full field path (e.g. <c>lines[2].quantity</c>).
        /// </para>
        /// <para>
        /// For <see cref="ErrorType.Unexpected"/> the message is replaced by <see cref="UnexpectedDetail"/>: it often comes
        /// from an exception and should not reach the client. Log the error on the server instead.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="error"/> is <see langword="null"/>.</exception>
        public ProblemDetails ToProblemDetails()
        {
            ArgumentNullException.ThrowIfNull(error);

            IReadOnlyList<Error> entries = error.Details.Count > 0
                ? [.. error.Flatten()]
                : (error.Field is null ? [] : [error]);

            var problem = entries.Count == 0
                ? new ProblemDetails()
                : new HttpValidationProblemDetails(
                    entries
                        .GroupBy(entry => entry.Field ?? string.Empty, StringComparer.Ordinal)
                        .ToDictionary(group => group.Key, group => group.Select(entry => entry.Message).ToArray(), StringComparer.Ordinal));

            problem.Status = error.Type.ToStatusCode();
            problem.Detail = error.Type == ErrorType.Unexpected ? UnexpectedDetail : error.Message;
            problem.Extensions["code"] = error.Code;

            if (entries.Count > 0)
            {
                problem.Extensions["details"] = entries
                    .Select(entry => new ProblemErrorDetail(entry.Code, entry.Field, entry.Message))
                    .ToArray();
            }

            return problem;
        }
    }
}
