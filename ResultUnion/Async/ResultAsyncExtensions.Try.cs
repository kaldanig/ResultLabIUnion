namespace ResultUnion;

public static partial class ResultAsyncExtensions
{
    // Static extension members for one specific exception type: called as Result.TryAsync(..., (TException ex) => ...).
    extension(Result)
    {
        /// <summary>
        /// Runs asynchronous code that may throw, turning only exceptions of type <typeparamref name="TException"/> into a failure;
        /// any other exception propagates through the returned task.
        /// </summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <typeparam name="TException">The exception type to translate. It is inferred from a typed lambda parameter.</typeparam>
        /// <remarks>
        /// <para>
        /// For example, translate only an optimistic-concurrency conflict:
        /// <c>Result.TryAsync(() =&gt; db.SaveChangesAsync(ct), (DbUpdateConcurrencyException ex) =&gt; Error.Concurrency("…"))</c>.
        /// </para>
        /// <para>
        /// <see cref="OperationCanceledException"/> is never caught. Arguments are validated when the method is called.
        /// A delegate returning <c>Task&lt;T&gt;</c> picks this overload; for a result without a value, pass a block lambda such as
        /// <c>async () =&gt; { await db.SaveChangesAsync(ct); }</c>.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="action"/> or <paramref name="onException"/> is <see langword="null"/>.</exception>
        public static Task<Result<T>> TryAsync<T, TException>(Func<Task<T>> action, Func<TException, Error> onException)
            where T : notnull
            where TException : Exception
        {
            ArgumentNullException.ThrowIfNull(action);
            ArgumentNullException.ThrowIfNull(onException);

            return Core(action, onException);

            static async Task<Result<T>> Core(Func<Task<T>> action, Func<TException, Error> onException)
            {
                T value;

                try
                {
                    value = await action().ConfigureAwait(false);
                }
                catch (TException exception) when (exception is not OperationCanceledException)
                {
                    return Result<T>.Failure(onException(exception));
                }

                return Result<T>.Success(value);
            }
        }

        /// <summary>
        /// Runs asynchronous code that may throw and produces no value, turning only exceptions of type <typeparamref name="TException"/>
        /// into a failure; any other exception propagates through the returned task.
        /// </summary>
        /// <typeparam name="TException">The exception type to translate. It is inferred from a typed lambda parameter.</typeparam>
        /// <remarks><see cref="OperationCanceledException"/> is never caught. Arguments are validated when the method is called.</remarks>
        /// <exception cref="ArgumentNullException"><paramref name="action"/> or <paramref name="onException"/> is <see langword="null"/>.</exception>
        public static Task<Result> TryAsync<TException>(Func<Task> action, Func<TException, Error> onException)
            where TException : Exception
        {
            ArgumentNullException.ThrowIfNull(action);
            ArgumentNullException.ThrowIfNull(onException);

            return Core(action, onException);

            static async Task<Result> Core(Func<Task> action, Func<TException, Error> onException)
            {
                try
                {
                    await action().ConfigureAwait(false);
                }
                catch (TException exception) when (exception is not OperationCanceledException)
                {
                    return Result.Failure(onException(exception));
                }

                return Result.Success();
            }
        }
    }
}
