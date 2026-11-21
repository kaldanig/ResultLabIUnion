using System.Globalization;
using System.Text;
using Google.Protobuf.WellKnownTypes;
using Google.Rpc;
using Grpc.Core;
using RpcStatus = Google.Rpc.Status;

namespace ResultUnion.Grpc;

/// <summary>Converts <see cref="Error"/> to gRPC's rich error model (<c>google.rpc.Status</c>) and back.</summary>
/// <remarks>
/// <para>An error becomes a status with:</para>
/// <list type="bullet">
///   <item><description>the code from <see cref="GrpcStatusCodeMapping.ToGrpcStatusCode(ErrorType)"/> and the message;</description></item>
///   <item><description>an <c>ErrorInfo</c>: <c>reason</c> is the error code in UPPER_SNAKE_CASE as gRPC requires
///   (<c>user.too_young</c> → <c>USER_TOO_YOUNG</c>); <c>metadata</c> holds the original <c>code</c>, the <c>type</c>,
///   the <see cref="Error.Arguments"/> as strings, and the original code of every field violation (<c>code:REASON</c>);</description></item>
///   <item><description>a <c>BadRequest</c> with one field violation per invalid input, from <see cref="Error.Flatten"/>, when
///   the error has a <see cref="Error.Field"/> or <see cref="Error.Details"/>;</description></item>
///   <item><description>a <c>RetryInfo</c> for <see cref="ErrorType.TooManyRequests"/> with a numeric <c>retryAfterSeconds</c> argument.</description></item>
/// </list>
/// <para>
/// For <see cref="ErrorType.Unexpected"/> the message is replaced by <see cref="UnexpectedMessage"/> and no arguments are
/// sent, as with HTTP. <see cref="ToError(RpcException)"/> restores the type, code, message, arguments (as strings),
/// field paths and field codes; the arguments of individual field violations are not transmitted.
/// </para>
/// </remarks>
public static class RpcErrorMapping
{
    /// <summary>The <c>ErrorInfo.domain</c> used when none is given.</summary>
    public const string DefaultDomain = "resultunion";

    /// <summary>The message sent for <see cref="ErrorType.Unexpected"/>, whose own message may expose internals.</summary>
    public const string UnexpectedMessage = "An unexpected error occurred.";

    private const string CodeKey = "code";
    private const string TypeKey = "type";
    private const string FieldCodePrefix = "code:";

    /// <param name="error">The error to convert.</param>
    extension(Error error)
    {
        /// <summary>Creates a <c>google.rpc.Status</c> describing the error.</summary>
        /// <param name="domain">The <c>ErrorInfo.domain</c>: the service or product that defines the error codes.</param>
        /// <exception cref="ArgumentNullException"><paramref name="error"/> is <see langword="null"/>.</exception>
        public RpcStatus ToRpcStatus(string domain = DefaultDomain)
        {
            ArgumentNullException.ThrowIfNull(error);
            ArgumentException.ThrowIfNullOrWhiteSpace(domain);

            var unexpected = error.Type == ErrorType.Unexpected;

            var info = new ErrorInfo
            {
                Reason = ToReason(error.Code), Domain = domain,
                Metadata =
                {
                    [CodeKey] = error.Code,
                    [TypeKey] = error.Type.ToString()
                }
            };

            if (!unexpected)
            {
                foreach (var (name, value) in error.Arguments)
                {
                    info.Metadata[name] = Format(value);
                }
            }

            var status = new RpcStatus
            {
                Code = (int)error.Type.ToGrpcStatusCode(),
                Message = unexpected ? UnexpectedMessage : error.Message,
            };

            IReadOnlyList<Error> violations = error.Details.Count > 0
                ? [.. error.Flatten()]
                : error.Field is null ? [] : [error];

            if (violations.Count > 0)
            {
                var badRequest = new BadRequest();
                foreach (var violation in violations)
                {
                    var reason = ToReason(violation.Code);
                    info.Metadata[FieldCodePrefix + reason] = violation.Code;
                    badRequest.FieldViolations.Add(new BadRequest.Types.FieldViolation
                    {
                        Field = violation.Field ?? string.Empty,
                        Description = violation.Message,
                        Reason = reason,
                    });
                }

                status.Details.Add(Any.Pack(info));
                status.Details.Add(Any.Pack(badRequest));
            }
            else
            {
                status.Details.Add(Any.Pack(info));
            }

            if (error.Type == ErrorType.TooManyRequests
                && error.Arguments.TryGetValue("retryAfterSeconds", out var retryAfter)
                && retryAfter is int or long or double or decimal)
            {
                status.Details.Add(Any.Pack(new RetryInfo
                {
                    RetryDelay = Duration.FromTimeSpan(TimeSpan.FromSeconds(Convert.ToDouble(retryAfter, CultureInfo.InvariantCulture))),
                }));
            }

            return status;
        }

        /// <summary>Creates the <see cref="RpcException"/> a gRPC service throws for the error, carrying the rich status.</summary>
        /// <param name="domain">The <c>ErrorInfo.domain</c>.</param>
        /// <exception cref="ArgumentNullException"><paramref name="error"/> is <see langword="null"/>.</exception>
        public RpcException ToRpcException(string domain = DefaultDomain) => error.ToRpcStatus(domain).ToRpcException();
    }

    /// <param name="exception">An exception received from a gRPC call.</param>
    extension(RpcException exception)
    {
        /// <summary>Turns an exception received from a gRPC call back into an <see cref="Error"/>.</summary>
        /// <remarks>
        /// <para>
        /// With a rich status from a ResultUnion service, the type, code, message, arguments and field violations are restored;
        /// argument values arrive as strings. Without one (any other gRPC server), the error has the code
        /// <c>grpc.&lt;status&gt;</c> (e.g. <c>grpc.deadline_exceeded</c>) and the type from
        /// <see cref="GrpcStatusCodeMapping.ToErrorType(StatusCode)"/>.
        /// </para>
        /// <para>Typical use: <c>Result.TryAsync(() =&gt; client.GetUserAsync(request).ResponseAsync, (RpcException ex) =&gt; ex.ToError())</c>.</para>
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="exception"/> is <see langword="null"/>.</exception>
        public Error ToError()
        {
            ArgumentNullException.ThrowIfNull(exception);

            var status = exception.GetRpcStatus();
            var statusCode = (StatusCode)(status?.Code ?? (int)exception.StatusCode);
            var info = status?.GetDetail<ErrorInfo>();

            var code = info?.Metadata.GetValueOrDefault(CodeKey)
                ?? (info is null ? $"grpc.{ToSnakeCase(statusCode.ToString())}" : info.Reason.ToLowerInvariant());
            var type = info is not null && System.Enum.TryParse<ErrorType>(info.Metadata.GetValueOrDefault(TypeKey), out var parsed)
                ? parsed
                : statusCode.ToErrorType();
            var message = (status?.Message ?? exception.Status.Detail) is { Length: > 0 } text ? text : statusCode.ToString();

            var error = Error.Create(code, message, type);

            if (info is not null)
            {
                foreach (var (name, value) in info.Metadata)
                {
                    if (name is not (CodeKey or TypeKey) && !name.StartsWith(FieldCodePrefix, StringComparison.Ordinal))
                    {
                        error = error.WithArgument(name, value);
                    }
                }
            }

            var violations = status?.GetDetail<BadRequest>()?.FieldViolations;
            if (violations is not { Count: > 0 })
            {
                return error;
            }

            var fieldErrors = violations
                .Select(violation =>
                {
                    var violationCode = info?.Metadata.GetValueOrDefault(FieldCodePrefix + violation.Reason)
                        ?? (violation.Reason is { Length: > 0 } reason ? reason.ToLowerInvariant() : code);
                    var fieldError = Error.Create(violationCode, violation.Description is { Length: > 0 } d ? d : message, type);
                    return violation.Field is { Length: > 0 } field ? fieldError.WithField(field) : fieldError;
                })
                .ToArray();

            // A single violation that is the error itself (an error with a Field) comes back as one error, not a group.
            if (fieldErrors is [var single] && single.Code == code)
            {
                return single.Field is null ? error : error.WithField(single.Field);
            }

            return WithArguments(Error.Validation(fieldErrors), error.Arguments);
        }
    }

    private static Error WithArguments(Error error, IReadOnlyDictionary<string, object?> arguments) =>
        arguments.Aggregate(error, (current, pair) => current.WithArgument(pair.Key, pair.Value));

    // user.too_young -> USER_TOO_YOUNG; anything that is not a letter or digit becomes '_'.
    internal static string ToReason(string code)
    {
        var builder = new StringBuilder(code.Length);
        foreach (var c in code)
        {
            builder.Append(char.IsAsciiLetterOrDigit(c) ? char.ToUpperInvariant(c) : '_');
        }

        return builder.ToString();
    }

    private static string ToSnakeCase(string pascal)
    {
        var builder = new StringBuilder(pascal.Length + 4);
        for (var i = 0; i < pascal.Length; i++)
        {
            if (i > 0 && char.IsUpper(pascal[i]))
            {
                builder.Append('_');
            }

            builder.Append(char.ToLowerInvariant(pascal[i]));
        }

        return builder.ToString();
    }

    private static string Format(object? value) => value switch
    {
        null => string.Empty,
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };
}
