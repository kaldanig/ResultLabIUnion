using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace ResultUnion.Http;

/// <summary>
/// Reads an <see cref="HttpResponseMessage"/> as a <see cref="Result"/> or <see cref="Result{T}"/>: a success status
/// gives the value from the JSON body, any other status gives an <see cref="Error"/> read from the RFC 9457 problem
/// details body. The reverse of ResultUnion.AspNetCore's <c>ToHttpResult</c>.
/// </summary>
/// <remarks>
/// <para>
/// From a ResultUnion server the error comes back as it was sent: <c>code</c>, <c>category</c> (the
/// <see cref="ErrorType"/>), message (<c>detail</c>), <c>arguments</c> and one field error per entry of <c>details</c>.
/// From any other server, the type follows the status code (400 Validation, 401 Unauthorized, 403 Forbidden,
/// 404 NotFound, 409 Conflict, 422 BusinessRule, 429 TooManyRequests, 408/502/503/504 Unavailable, anything else
/// Unexpected), the code is <c>http.&lt;status&gt;</c> (e.g. <c>http.not_found</c>), the message is <c>detail</c>,
/// <c>title</c> or the reason phrase, and ASP.NET Core's <c>errors</c> member becomes field errors. A
/// <c>Retry-After</c> header becomes the <c>retryAfterSeconds</c> argument when the body does not carry it.
/// </para>
/// <para>
/// Only the response is interpreted. A request that fails to arrive (<see cref="HttpRequestException"/>, a timeout) and a
/// success body that is not valid JSON for <c>T</c> (<see cref="JsonException"/>) surface as exceptions; translate the
/// ones you expect with <c>Result.TryAsync</c>. Cancellation is never turned into a failure.
/// </para>
/// <para>
/// The methods on <see cref="HttpResponseMessage"/> leave the response to the caller; the methods on
/// <c>Task&lt;HttpResponseMessage&gt;</c> (<c>http.GetAsync(url).ReadResultAsync&lt;T&gt;()</c>) dispose it.
/// </para>
/// </remarks>
public static class HttpResultExtensions
{
    private const string RetryAfterSeconds = "retryAfterSeconds";
    private const string RS0026 = "Different receivers in one static class, not real overloads. Add new options as new overloads, never as new optional parameters.";

    /// <param name="response">The response to read; it is not disposed.</param>
    extension(HttpResponseMessage response)
    {
        /// <summary>Reads the response: the value deserialized from the JSON body on a success status, or the error from the problem details body otherwise.</summary>
        /// <typeparam name="T">The type of the value in a successful response body.</typeparam>
        /// <exception cref="ArgumentNullException"><paramref name="response"/> is <see langword="null"/>.</exception>
        /// <exception cref="JsonException">A successful response's body is not valid JSON for <typeparamref name="T"/>.</exception>
        /// <exception cref="InvalidOperationException">A successful response's body is empty or <c>null</c>.</exception>
        [SuppressMessage("ApiDesign", "RS0026", Justification = RS0026)]
        public Task<Result<T>> ReadResultAsync<T>(CancellationToken cancellationToken = default)
            where T : notnull
        {
            ArgumentNullException.ThrowIfNull(response);

            return ReadValueAsync<T>(response, cancellationToken);
        }

        /// <summary>Reads the response as a result without a value: a success on a success status (the body is ignored), or the error from the problem details body otherwise.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="response"/> is <see langword="null"/>.</exception>
        [SuppressMessage("ApiDesign", "RS0026", Justification = RS0026)]
        public Task<Result> ReadResultAsync(CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(response);

            return ReadOutcomeAsync(response, cancellationToken);
        }

        /// <summary>Reads the error described by a failed response: its problem details body, or its status code when there is none.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="response"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">The response has a success status code.</exception>
        public Task<Error> ReadErrorAsync(CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(response);
            if (response.IsSuccessStatusCode)
                throw new InvalidOperationException($"The response has the success status {(int)response.StatusCode}; it describes no error.");

            return ReadErrorCoreAsync(response, cancellationToken);
        }
    }

    /// <param name="source">The pending response to read; it is disposed after reading.</param>
    extension(Task<HttpResponseMessage> source)
    {
        /// <summary>Awaits the response, reads it as a <see cref="Result{T}"/> and disposes it.</summary>
        /// <typeparam name="T">The type of the value in a successful response body.</typeparam>
        /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
        [SuppressMessage("ApiDesign", "RS0026", Justification = RS0026)]
        public Task<Result<T>> ReadResultAsync<T>(CancellationToken cancellationToken = default)
            where T : notnull
        {
            ArgumentNullException.ThrowIfNull(source);

            return Core(source, cancellationToken);

            static async Task<Result<T>> Core(Task<HttpResponseMessage> source, CancellationToken cancellationToken)
            {
                using var response = await source.ConfigureAwait(false);
                return await ReadValueAsync<T>(response, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>Awaits the response, reads it as a <see cref="Result"/> and disposes it.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
        [SuppressMessage("ApiDesign", "RS0026", Justification = RS0026)]
        public Task<Result> ReadResultAsync(CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(source);

            return Core(source, cancellationToken);

            static async Task<Result> Core(Task<HttpResponseMessage> source, CancellationToken cancellationToken)
            {
                using var response = await source.ConfigureAwait(false);
                return await ReadOutcomeAsync(response, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async Task<Result<T>> ReadValueAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
        where T : notnull
    {
        if (!response.IsSuccessStatusCode)
            return Result<T>.Failure(await ReadErrorCoreAsync(response, cancellationToken).ConfigureAwait(false));

        if (response.StatusCode == HttpStatusCode.NoContent || response.Content.Headers.ContentLength == 0)
            throw new InvalidOperationException($"The response ({(int)response.StatusCode}) has no body to read a {typeof(T).Name} from.");

        var value = await response.Content.ReadFromJsonAsync<T>(cancellationToken).ConfigureAwait(false);
        return Result<T>.Success(value ?? throw new InvalidOperationException($"The response body is null; a {typeof(T).Name} was expected."));
    }

    private static async Task<Result> ReadOutcomeAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        response.IsSuccessStatusCode
            ? Result.Success()
            : Result.Failure(await ReadErrorCoreAsync(response, cancellationToken).ConfigureAwait(false));

    private static async Task<Error> ReadErrorCoreAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        using var problem = await ReadProblemAsync(response, cancellationToken).ConfigureAwait(false);
        var body = problem?.RootElement;

        var status = (int)response.StatusCode;
        var type = Enum.TryParse<ErrorType>(String(body, "category"), ignoreCase: false, out var category) && Enum.IsDefined(category)
            ? category
            : TypeFor(status);
        var code = String(body, "code") ?? CodeFor(response.StatusCode);
        var message = String(body, "detail") ?? String(body, "title") ?? response.ReasonPhrase ?? $"HTTP {status}";

        var error = AddArguments(Error.Create(code, message, type), body);
        if (!error.Arguments.ContainsKey(RetryAfterSeconds) && RetryAfter(response) is { } seconds)
            error = error.WithArgument(RetryAfterSeconds, seconds);

        var details = Details(body, type);
        return details switch
        {
            [] => error,
            // ResultUnion sends a single field error as itself plus its one detail: restore the field on the error.
            [var single] when single.Code == error.Code && single.Message == error.Message =>
                single.Field is null ? error : error.WithField(single.Field),
            // From another server, the one field message says more than the generic title.
            [var single] => single,
            _ when type == ErrorType.Validation => Error.Validation(details),
            // A group of another type cannot be rebuilt with its details through the public API (ResultUnion only groups
            // validation errors); keep the top-level error, which still carries the code, type and message.
            _ => error,
        };
    }

    private static async Task<JsonDocument?> ReadProblemAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (mediaType is null || !(mediaType.EndsWith("/json", StringComparison.OrdinalIgnoreCase) || mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase)))
            return null;

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            try
            {
                var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
                if (document.RootElement.ValueKind == JsonValueKind.Object)
                    return document;

                document.Dispose();
                return null;
            }
            catch (JsonException)
            {
                return null; // an error page that claims to be JSON: fall back to the status code
            }
        }
    }

    private static List<Error> Details(JsonElement? body, ErrorType type)
    {
        var details = new List<Error>();
        if (body is not { } root)
            return details;

        if (root.TryGetProperty("details", out var entries) && entries.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in entries.EnumerateArray())
            {
                var code = entry.ValueKind == JsonValueKind.Object ? String(entry, "code") : null;
                var message = entry.ValueKind == JsonValueKind.Object ? String(entry, "message") : null;
                if (code is null || message is null)
                    continue;

                var detail = AddArguments(Error.Create(code, message, type), entry);
                var field = String(entry, "field");
                details.Add(field is null ? detail : detail.WithField(field));
            }
        }
        else if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
        {
            // ASP.NET Core's own validation problems (model binding, [ApiController]): messages per field, no codes.
            foreach (var property in errors.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.Array))
            {
                foreach (var message in property.Value.EnumerateArray().Where(m => m.ValueKind == JsonValueKind.String))
                {
                    if (string.IsNullOrWhiteSpace(message.GetString()))
                        continue;

                    var detail = Error.Create("validation.failed", message.GetString()!, type);
                    details.Add(string.IsNullOrEmpty(property.Name) ? detail : detail.WithField(property.Name));
                }
            }
        }

        return details;
    }

    private static Error AddArguments(Error error, JsonElement? element)
    {
        if (element is not { } root || !root.TryGetProperty("arguments", out var arguments) || arguments.ValueKind != JsonValueKind.Object)
            return error;

        foreach (var argument in arguments.EnumerateObject())
        {
            if (!string.IsNullOrWhiteSpace(argument.Name))
                error = error.WithArgument(argument.Name, Value(argument.Value));
        }

        return error;
    }

    // The same reading as ErrorJsonConverter: whole numbers as int (or long), other numbers as double, strings, Booleans,
    // null, and anything else as a JsonElement.
    private static object? Value(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        JsonValueKind.Number when value.TryGetInt32(out var number) => number,
        JsonValueKind.Number when value.TryGetInt64(out var number) => number,
        JsonValueKind.Number => value.GetDouble(),
        _ => value.Clone(),
    };

    private static string? String(JsonElement? element, string name) =>
        element is { } root && root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()
            : null;

    private static int? RetryAfter(HttpResponseMessage response) => response.Headers.RetryAfter switch
    {
        { Delta: { } delta } => (int)Math.Ceiling(Math.Max(0, delta.TotalSeconds)),
        { Date: { } date } => (int)Math.Ceiling(Math.Max(0, (date - DateTimeOffset.UtcNow).TotalSeconds)),
        _ => null,
    };

    private static ErrorType TypeFor(int status) => status switch
    {
        400 => ErrorType.Validation,
        401 => ErrorType.Unauthorized,
        403 => ErrorType.Forbidden,
        404 => ErrorType.NotFound,
        409 => ErrorType.Conflict,
        422 => ErrorType.BusinessRule,
        429 => ErrorType.TooManyRequests,
        408 or 502 or 503 or 504 => ErrorType.Unavailable,
        _ => ErrorType.Unexpected,
    };

    // "http.not_found" from HttpStatusCode.NotFound; "http.599" for a status without a name.
    private static string CodeFor(HttpStatusCode status)
    {
        var name = Enum.IsDefined(status) ? status.ToString() : null;
        return name is null || char.IsDigit(name[0])
            ? $"http.{(int)status}"
            : "http." + JsonNamingPolicy.SnakeCaseLower.ConvertName(name);
    }
}
