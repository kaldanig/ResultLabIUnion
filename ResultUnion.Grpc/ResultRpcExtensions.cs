using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Grpc.Core;
using ResultUnion.Diagnostics;

namespace ResultUnion.Grpc;

/// <summary>Ends a pipeline in a gRPC service method: the reply on success, an <see cref="RpcException"/> with a rich status on failure.</summary>
/// <remarks>
/// <para>
/// gRPC methods return the reply message itself, and the protocol reports failures through the status, so the boundary
/// throws: <c>return await users.FindAsync(request.Email, ct).MapAsync(UserReply.From).GetValueOrThrowRpcAsync();</c>.
/// The exception is built with <see cref="RpcErrorMapping.ToRpcException(Error, string)"/>, and the current span (the
/// call's server span) is tagged with the error code and category through <see cref="ResultTelemetry.TagActivity"/>.
/// </para>
/// <para>Use these only at the service boundary; inside the application, keep working with results.</para>
/// </remarks>
public static class ResultRpcExtensions
{
    /// <typeparam name="T">The type of the success value, usually the reply message.</typeparam>
    /// <param name="result">The result to end the call with.</param>
    extension<T>(Result<T> result)
        where T : notnull
    {
        /// <summary>Returns the value on success; throws an <see cref="RpcException"/> for the error on failure.</summary>
        /// <param name="domain">The <c>ErrorInfo.domain</c>.</param>
        /// <exception cref="RpcException">The result is a failure.</exception>
        [SuppressMessage("ApiDesign", "RS0026", Justification = "Different receivers in one static class, not real overloads. Add new options as new overloads, never as new optional parameters.")]
        public T GetValueOrThrowRpc(string domain = RpcErrorMapping.DefaultDomain) =>
            result.TryGetResultValue(out var value) ? value : throw ToRpcFailure(result.Error, domain);
    }

    /// <param name="result">The result to end the call with.</param>
    extension(Result result)
    {
        /// <summary>Returns the reply built by <paramref name="onSuccess"/> on success; throws an <see cref="RpcException"/> on failure.</summary>
        /// <remarks>For methods without a meaningful reply: <c>result.GetValueOrThrowRpc(() =&gt; new Empty())</c>.</remarks>
        /// <param name="onSuccess">Creates the reply.</param>
        /// <param name="domain">The <c>ErrorInfo.domain</c>.</param>
        /// <exception cref="ArgumentNullException"><paramref name="onSuccess"/> is <see langword="null"/>.</exception>
        /// <exception cref="RpcException">The result is a failure.</exception>
        [SuppressMessage("ApiDesign", "RS0026", Justification = "Different receivers in one static class, not real overloads. Add new options as new overloads, never as new optional parameters.")]
        public TReply GetValueOrThrowRpc<TReply>(Func<TReply> onSuccess, string domain = RpcErrorMapping.DefaultDomain)
        {
            ArgumentNullException.ThrowIfNull(onSuccess);

            return result.TryGetError(out var error) ? throw ToRpcFailure(error, domain) : onSuccess();
        }
    }

    // The exception for a failure that leaves the service; tags the current span (the call's server span) with the
    // error code and category, so a trace shows which error the call ended with.
    private static RpcException ToRpcFailure(Error error, string domain)
    {
        var exception = error.ToRpcException(domain);
        ResultTelemetry.TagActivity(Activity.Current, error);
        return exception;
    }

    /// <typeparam name="T">The type of the success value, usually the reply message.</typeparam>
    /// <param name="source">The pending result to end the call with.</param>
    extension<T>(Task<Result<T>> source)
        where T : notnull
    {
        /// <summary>Awaits the result, then returns the value or throws an <see cref="RpcException"/> for the error.</summary>
        /// <param name="domain">The <c>ErrorInfo.domain</c>.</param>
        /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
        [SuppressMessage("ApiDesign", "RS0026", Justification = "Different receivers in one static class, not real overloads. Add new options as new overloads, never as new optional parameters.")]
        public Task<T> GetValueOrThrowRpcAsync(string domain = RpcErrorMapping.DefaultDomain)
        {
            ArgumentNullException.ThrowIfNull(source);

            return Core(source, domain);

            static async Task<T> Core(Task<Result<T>> source, string domain) =>
                (await source.ConfigureAwait(false)).GetValueOrThrowRpc(domain);
        }
    }

    /// <param name="source">The pending result to end the call with.</param>
    extension(Task<Result> source)
    {
        /// <summary>Awaits the result, then returns the reply built by <paramref name="onSuccess"/> or throws an <see cref="RpcException"/>.</summary>
        /// <param name="onSuccess">Creates the reply.</param>
        /// <param name="domain">The <c>ErrorInfo.domain</c>.</param>
        /// <exception cref="ArgumentNullException"><paramref name="source"/> or <paramref name="onSuccess"/> is <see langword="null"/>.</exception>
        [SuppressMessage("ApiDesign", "RS0026", Justification = "Different receivers in one static class, not real overloads. Add new options as new overloads, never as new optional parameters.")]
        public Task<TReply> GetValueOrThrowRpcAsync<TReply>(Func<TReply> onSuccess, string domain = RpcErrorMapping.DefaultDomain)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(onSuccess);

            return Core(source, onSuccess, domain);

            static async Task<TReply> Core(Task<Result> source, Func<TReply> onSuccess, string domain) =>
                (await source.ConfigureAwait(false)).GetValueOrThrowRpc(onSuccess, domain);
        }
    }
}
