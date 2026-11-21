using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ResultUnion.AspNetCore;

/// <summary>Turns results into Minimal API responses: the success value as the given response, a failure as a problem response.</summary>
/// <remarks>
/// <para>
/// Failures go through <see cref="ProblemDetailsMapping.ToProblemDetails(Error)"/>: the status comes from
/// <see cref="Error.Type"/>, and the body is RFC 9457 problem details with the error <c>code</c>.
/// </para>
/// <para>
/// The responses are <b>typed</b>, e.g. <c>Results&lt;Ok&lt;T&gt;, ProblemHttpResult&gt;</c>, so OpenAPI documents the success
/// response of an endpoint without <c>.Produces&lt;T&gt;()</c>. Declare the possible problem responses with
/// <see cref="EndpointProblemMetadataExtensions.ProducesErrors{TBuilder}(TBuilder, ErrorType[])"/>: a problem's status is only known at run time.
/// </para>
/// <code>
/// app.MapPost("/users", (RegisterUser command, ISender sender) =>
///         sender.Send(command).ToHttpResultAsync(user => TypedResults.Created($"/users/{user.Id}", user)))
///     .ProducesErrors(ErrorType.Validation, ErrorType.Conflict);
/// </code>
/// </remarks>
public static class ResultHttpExtensions
{
    /// <param name="error">The error to respond with.</param>
    extension(Error error)
    {
        /// <summary>Creates a problem response for the error.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="error"/> is <see langword="null"/>.</exception>
        public ProblemHttpResult ToHttpResult() => TypedResults.Problem(error.ToProblemDetails());
    }

    /// <typeparam name="T">The type of the success value.</typeparam>
    /// <param name="result">The result to respond with.</param>
    extension<T>(Result<T> result)
        where T : notnull
    {
        /// <summary>Creates <c>200 OK</c> with the value on success, or a problem response on failure.</summary>
        public Results<Ok<T>, ProblemHttpResult> ToHttpResult() =>
            result.Match<Results<Ok<T>, ProblemHttpResult>>(value => TypedResults.Ok(value), error => error.ToHttpResult());

        /// <summary>Creates the typed response built by <paramref name="onSuccess"/> on success, or a problem response on failure.</summary>
        /// <typeparam name="TSuccess">The success response type, e.g. <c>Created&lt;UserDto&gt;</c>; it appears in OpenAPI.</typeparam>
        /// <remarks>For example <c>result.ToHttpResult(user => TypedResults.Created($"/users/{user.Id}", user))</c>.</remarks>
        /// <exception cref="ArgumentNullException"><paramref name="onSuccess"/> is <see langword="null"/>, or returns <see langword="null"/>.</exception>
        public Results<TSuccess, ProblemHttpResult> ToHttpResult<TSuccess>(Func<T, TSuccess> onSuccess)
            where TSuccess : IResult
        {
            ArgumentNullException.ThrowIfNull(onSuccess);

            return result.Match<Results<TSuccess, ProblemHttpResult>>(
                value => onSuccess(value) ?? throw new ArgumentNullException(nameof(onSuccess), "The success response must not be null."),
                error => error.ToHttpResult());
        }

        /// <summary>Creates the response built by <paramref name="onSuccess"/> on success, or a problem response on failure; untyped.</summary>
        /// <remarks>
        /// For a success that can be one of several responses (for example <c>Ok</c> or <c>Redirect</c>). The result is a plain
        /// <see cref="IResult"/>, so OpenAPI cannot see it: prefer the typed overload when there is one success response.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="onSuccess"/> is <see langword="null"/>, or returns <see langword="null"/>.</exception>
        public IResult ToHttpResult(Func<T, IResult> onSuccess)
        {
            ArgumentNullException.ThrowIfNull(onSuccess);

            return result.Match(
                value => onSuccess(value) ?? throw new ArgumentNullException(nameof(onSuccess), "The success response must not be null."),
                error => error.ToHttpResult());
        }
    }

    /// <param name="result">The result to respond with.</param>
    extension(Result result)
    {
        /// <summary>Creates <c>204 No Content</c> on success, or a problem response on failure.</summary>
        public Results<NoContent, ProblemHttpResult> ToHttpResult() =>
            result.Match<Results<NoContent, ProblemHttpResult>>(() => TypedResults.NoContent(), error => error.ToHttpResult());

        /// <summary>Creates the typed response built by <paramref name="onSuccess"/> on success, or a problem response on failure.</summary>
        /// <typeparam name="TSuccess">The success response type, e.g. <c>Accepted</c>; it appears in OpenAPI.</typeparam>
        /// <exception cref="ArgumentNullException"><paramref name="onSuccess"/> is <see langword="null"/>, or returns <see langword="null"/>.</exception>
        public Results<TSuccess, ProblemHttpResult> ToHttpResult<TSuccess>(Func<TSuccess> onSuccess)
            where TSuccess : IResult
        {
            ArgumentNullException.ThrowIfNull(onSuccess);

            return result.Match<Results<TSuccess, ProblemHttpResult>>(
                () => onSuccess() ?? throw new ArgumentNullException(nameof(onSuccess), "The success response must not be null."),
                error => error.ToHttpResult());
        }

        /// <summary>Creates the response built by <paramref name="onSuccess"/> on success, or a problem response on failure; untyped.</summary>
        /// <remarks>For a success that can be one of several responses. Prefer the typed overload when there is one success response.</remarks>
        /// <exception cref="ArgumentNullException"><paramref name="onSuccess"/> is <see langword="null"/>, or returns <see langword="null"/>.</exception>
        public IResult ToHttpResult(Func<IResult> onSuccess)
        {
            ArgumentNullException.ThrowIfNull(onSuccess);

            return result.Match(
                () => onSuccess() ?? throw new ArgumentNullException(nameof(onSuccess), "The success response must not be null."),
                error => error.ToHttpResult());
        }
    }

    /// <typeparam name="T">The type of the success value.</typeparam>
    /// <param name="source">The pending result to respond with.</param>
    extension<T>(Task<Result<T>> source)
        where T : notnull
    {
        /// <summary>Awaits the result, then creates <c>200 OK</c> with the value, or a problem response.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
        public Task<Results<Ok<T>, ProblemHttpResult>> ToHttpResultAsync()
        {
            ArgumentNullException.ThrowIfNull(source);

            return Core(source);

            static async Task<Results<Ok<T>, ProblemHttpResult>> Core(Task<Result<T>> source) =>
                (await source.ConfigureAwait(false)).ToHttpResult();
        }

        /// <summary>Awaits the result, then creates the typed response built by <paramref name="onSuccess"/>, or a problem response.</summary>
        /// <typeparam name="TSuccess">The success response type; it appears in OpenAPI.</typeparam>
        /// <exception cref="ArgumentNullException"><paramref name="source"/> or <paramref name="onSuccess"/> is <see langword="null"/>.</exception>
        public Task<Results<TSuccess, ProblemHttpResult>> ToHttpResultAsync<TSuccess>(Func<T, TSuccess> onSuccess)
            where TSuccess : IResult
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(onSuccess);

            return Core(source, onSuccess);

            static async Task<Results<TSuccess, ProblemHttpResult>> Core(Task<Result<T>> source, Func<T, TSuccess> onSuccess) =>
                (await source.ConfigureAwait(false)).ToHttpResult(onSuccess);
        }

        /// <summary>Awaits the result, then creates the untyped response built by <paramref name="onSuccess"/>, or a problem response.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="source"/> or <paramref name="onSuccess"/> is <see langword="null"/>.</exception>
        public Task<IResult> ToHttpResultAsync(Func<T, IResult> onSuccess)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(onSuccess);

            return Core(source, onSuccess);

            static async Task<IResult> Core(Task<Result<T>> source, Func<T, IResult> onSuccess) =>
                (await source.ConfigureAwait(false)).ToHttpResult(onSuccess);
        }
    }

    /// <param name="source">The pending result to respond with.</param>
    extension(Task<Result> source)
    {
        /// <summary>Awaits the result, then creates <c>204 No Content</c>, or a problem response.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
        public Task<Results<NoContent, ProblemHttpResult>> ToHttpResultAsync()
        {
            ArgumentNullException.ThrowIfNull(source);

            return Core(source);

            static async Task<Results<NoContent, ProblemHttpResult>> Core(Task<Result> source) =>
                (await source.ConfigureAwait(false)).ToHttpResult();
        }

        /// <summary>Awaits the result, then creates the typed response built by <paramref name="onSuccess"/>, or a problem response.</summary>
        /// <typeparam name="TSuccess">The success response type; it appears in OpenAPI.</typeparam>
        /// <exception cref="ArgumentNullException"><paramref name="source"/> or <paramref name="onSuccess"/> is <see langword="null"/>.</exception>
        public Task<Results<TSuccess, ProblemHttpResult>> ToHttpResultAsync<TSuccess>(Func<TSuccess> onSuccess)
            where TSuccess : IResult
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(onSuccess);

            return Core(source, onSuccess);

            static async Task<Results<TSuccess, ProblemHttpResult>> Core(Task<Result> source, Func<TSuccess> onSuccess) =>
                (await source.ConfigureAwait(false)).ToHttpResult(onSuccess);
        }

        /// <summary>Awaits the result, then creates the untyped response built by <paramref name="onSuccess"/>, or a problem response.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="source"/> or <paramref name="onSuccess"/> is <see langword="null"/>.</exception>
        public Task<IResult> ToHttpResultAsync(Func<IResult> onSuccess)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(onSuccess);

            return Core(source, onSuccess);

            static async Task<IResult> Core(Task<Result> source, Func<IResult> onSuccess) =>
                (await source.ConfigureAwait(false)).ToHttpResult(onSuccess);
        }
    }
}
