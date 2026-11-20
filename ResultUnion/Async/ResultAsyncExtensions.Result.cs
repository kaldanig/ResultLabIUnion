namespace ResultUnion;

public static partial class ResultAsyncExtensions
{
    // Static extension members: called as Result.TryAsync(...), next to the synchronous Result.Try(...).
    extension(Result)
    {
        /// <summary>Runs asynchronous code that may throw, turning an exception into a failure built by <paramref name="onException"/>.</summary>
        /// <remarks>
        /// <para>
        /// Exceptions are observed whether <paramref name="action"/> throws before returning its task or the task faults later.
        /// <see cref="OperationCanceledException"/> (including <see cref="TaskCanceledException"/>) is never caught: cancellation is not a business failure.
        /// </para>
        /// <para>
        /// Arguments are validated when the method is called. An exception thrown by <paramref name="onException"/>, or the
        /// <see cref="ArgumentNullException"/> raised when the task's value is <see langword="null"/>, propagates through the returned task.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="action"/> or <paramref name="onException"/> is <see langword="null"/>.</exception>
        public static Task<Result<T>> TryAsync<T>(Func<Task<T>> action, Func<Exception, Error> onException)
            where T : notnull
        {
            ArgumentNullException.ThrowIfNull(action);
            ArgumentNullException.ThrowIfNull(onException);

            return Core(action, onException);

            static async Task<Result<T>> Core(Func<Task<T>> action, Func<Exception, Error> onException)
            {
                T value;

                try
                {
                    value = await action().ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    return Result<T>.Failure(onException(exception));
                }

                return Result<T>.Success(value);
            }
        }

        /// <summary>Runs asynchronous code that may throw and produces no value, turning an exception into a failure built by <paramref name="onException"/>.</summary>
        /// <remarks>
        /// <see cref="OperationCanceledException"/> is never caught, and an exception thrown by <paramref name="onException"/> propagates through the returned task.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="action"/> or <paramref name="onException"/> is <see langword="null"/>.</exception>
        public static Task<Result> TryAsync(Func<Task> action, Func<Exception, Error> onException)
        {
            ArgumentNullException.ThrowIfNull(action);
            ArgumentNullException.ThrowIfNull(onException);

            return Core(action, onException);

            static async Task<Result> Core(Func<Task> action, Func<Exception, Error> onException)
            {
                try
                {
                    await action().ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    return Result.Failure(onException(exception));
                }

                return Result.Success();
            }
        }
    }

    /// <param name="result">The result to continue from.</param>
    extension(Result result)
    {
        /// <summary>Chains an asynchronous operation that may fail and produces no value, when this result is a success; failures are propagated unchanged.</summary>
        public Task<Result> BindAsync(Func<Task<Result>> next)
        {
            ArgumentNullException.ThrowIfNull(next);

            if (result.IsFailure)
            {
                return Task.FromResult(result);
            }

            return Core(next);

            static async Task<Result> Core(Func<Task<Result>> next) =>
                await next().ConfigureAwait(false);
        }

        /// <summary>Chains an asynchronous value-producing operation that may fail, when this result is a success; failures are propagated unchanged.</summary>
        public Task<Result<T>> BindAsync<T>(Func<Task<Result<T>>> next)
            where T : notnull
        {
            ArgumentNullException.ThrowIfNull(next);

            if (result.TryGetError(out var error))
            {
                return Task.FromResult(Result<T>.Failure(error));
            }

            return Core(next);

            static async Task<Result<T>> Core(Func<Task<Result<T>>> next) =>
                await next().ConfigureAwait(false);
        }

        /// <summary>Turns a successful result into a failure carrying <paramref name="error"/> when the asynchronous <paramref name="predicate"/> is not satisfied.</summary>
        /// <remarks>Failures are propagated unchanged and <paramref name="predicate"/> is not invoked.</remarks>
        public Task<Result> EnsureAsync(Func<Task<bool>> predicate, Error error)
        {
            ArgumentNullException.ThrowIfNull(predicate);
            ArgumentNullException.ThrowIfNull(error);

            if (result.IsFailure)
            {
                return Task.FromResult(result);
            }

            return Core(predicate, error);

            static async Task<Result> Core(Func<Task<bool>> predicate, Error error) =>
                await predicate().ConfigureAwait(false)
                    ? Result.Success()
                    : Result.Failure(error);
        }

        /// <summary>Turns a successful result into a failure built by <paramref name="errorFactory"/> when the asynchronous <paramref name="predicate"/> is not satisfied.</summary>
        /// <remarks>Failures are propagated unchanged and neither delegate is invoked; <paramref name="errorFactory"/> is invoked only when <paramref name="predicate"/> fails.</remarks>
        public Task<Result> EnsureAsync(Func<Task<bool>> predicate, Func<Error> errorFactory)
        {
            ArgumentNullException.ThrowIfNull(predicate);
            ArgumentNullException.ThrowIfNull(errorFactory);

            if (result.IsFailure)
            {
                return Task.FromResult(result);
            }

            return Core(predicate, errorFactory);

            static async Task<Result> Core(Func<Task<bool>> predicate, Func<Error> errorFactory) =>
                await predicate().ConfigureAwait(false)
                    ? Result.Success()
                    : Result.Failure(errorFactory());
        }

        /// <summary>Transforms the error of a failed result with an asynchronous mapper; successes are propagated unchanged.</summary>
        public Task<Result> MapErrorAsync(Func<Error, Task<Error>> mapper)
        {
            ArgumentNullException.ThrowIfNull(mapper);

            if (!result.TryGetError(out var error))
            {
                return Task.FromResult(result);
            }

            return Core(error, mapper);

            static async Task<Result> Core(Error error, Func<Error, Task<Error>> mapper) =>
                Result.Failure(await mapper(error).ConfigureAwait(false));
        }

        /// <summary>Awaits an asynchronous side effect when the result is a success, then returns the result unchanged.</summary>
        public Task<Result> TapAsync(Func<Task> action)
        {
            ArgumentNullException.ThrowIfNull(action);

            if (result.IsFailure)
            {
                return Task.FromResult(result);
            }

            return Core(result, action);

            static async Task<Result> Core(Result result, Func<Task> action)
            {
                await action().ConfigureAwait(false);

                return result;
            }
        }

        /// <summary>Awaits an asynchronous side effect with the error when the result is a failure, then returns the result unchanged.</summary>
        public Task<Result> TapErrorAsync(Func<Error, Task> action)
        {
            ArgumentNullException.ThrowIfNull(action);

            if (!result.TryGetError(out var error))
            {
                return Task.FromResult(result);
            }

            return Core(result, error, action);

            static async Task<Result> Core(Result result, Error error, Func<Error, Task> action)
            {
                await action(error).ConfigureAwait(false);

                return result;
            }
        }

        /// <summary>Projects the result into a single value by awaiting the asynchronous branch that matches its state.</summary>
        public Task<TResult> MatchAsync<TResult>(Func<Task<TResult>> onSuccess, Func<Error, Task<TResult>> onFailure)
        {
            ArgumentNullException.ThrowIfNull(onSuccess);
            ArgumentNullException.ThrowIfNull(onFailure);

            return Core(result, onSuccess, onFailure);

            static async Task<TResult> Core(Result result, Func<Task<TResult>> onSuccess, Func<Error, Task<TResult>> onFailure) =>
                result.TryGetError(out var error)
                    ? await onFailure(error).ConfigureAwait(false)
                    : await onSuccess().ConfigureAwait(false);
        }

        /// <summary>Awaits the asynchronous branch that matches the state of the result.</summary>
        public Task MatchAsync(Func<Task> onSuccess, Func<Error, Task> onFailure)
        {
            ArgumentNullException.ThrowIfNull(onSuccess);
            ArgumentNullException.ThrowIfNull(onFailure);

            return Core(result, onSuccess, onFailure);

            static async Task Core(Result result, Func<Task> onSuccess, Func<Error, Task> onFailure)
            {
                if (result.TryGetError(out var error))
                {
                    await onFailure(error).ConfigureAwait(false);
                }
                else
                {
                    await onSuccess().ConfigureAwait(false);
                }
            }
        }
    }

    /// <param name="source">The pending result to continue from.</param>
    extension(Task<Result> source)
    {
        /// <summary>Awaits the result, then chains an operation that may fail and produces no value; failures are propagated unchanged.</summary>
        public Task<Result> BindAsync(Func<Result> next)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(next);

            return Core(source, next);

            static async Task<Result> Core(Task<Result> source, Func<Result> next) =>
                (await source.ConfigureAwait(false)).Bind(next);
        }

        /// <summary>Awaits the result, then chains an asynchronous operation that may fail and produces no value; failures are propagated unchanged.</summary>
        public Task<Result> BindAsync(Func<Task<Result>> next)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(next);

            return Core(source, next);

            static async Task<Result> Core(Task<Result> source, Func<Task<Result>> next) =>
                await (await source.ConfigureAwait(false)).BindAsync(next).ConfigureAwait(false);
        }

        /// <summary>Awaits the result, then chains a value-producing operation that may fail; failures are propagated unchanged.</summary>
        public Task<Result<T>> BindAsync<T>(Func<Result<T>> next)
            where T : notnull
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(next);

            return Core(source, next);

            static async Task<Result<T>> Core(Task<Result> source, Func<Result<T>> next) =>
                (await source.ConfigureAwait(false)).Bind(next);
        }

        /// <summary>Awaits the result, then chains an asynchronous value-producing operation that may fail; failures are propagated unchanged.</summary>
        public Task<Result<T>> BindAsync<T>(Func<Task<Result<T>>> next)
            where T : notnull
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(next);

            return Core(source, next);

            static async Task<Result<T>> Core(Task<Result> source, Func<Task<Result<T>>> next) =>
                await (await source.ConfigureAwait(false)).BindAsync(next).ConfigureAwait(false);
        }

        /// <summary>Awaits the result, then turns a success into a failure carrying <paramref name="error"/> when <paramref name="predicate"/> is not satisfied.</summary>
        public Task<Result> EnsureAsync(Func<bool> predicate, Error error)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(predicate);
            ArgumentNullException.ThrowIfNull(error);

            return Core(source, predicate, error);

            static async Task<Result> Core(Task<Result> source, Func<bool> predicate, Error error) =>
                (await source.ConfigureAwait(false)).Ensure(predicate, error);
        }

        /// <summary>Awaits the result, then turns a success into a failure carrying <paramref name="error"/> when the asynchronous <paramref name="predicate"/> is not satisfied.</summary>
        public Task<Result> EnsureAsync(Func<Task<bool>> predicate, Error error)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(predicate);
            ArgumentNullException.ThrowIfNull(error);

            return Core(source, predicate, error);

            static async Task<Result> Core(Task<Result> source, Func<Task<bool>> predicate, Error error) =>
                await (await source.ConfigureAwait(false)).EnsureAsync(predicate, error).ConfigureAwait(false);
        }

        /// <summary>Awaits the result, then turns a success into a failure built by <paramref name="errorFactory"/> when <paramref name="predicate"/> is not satisfied.</summary>
        public Task<Result> EnsureAsync(Func<bool> predicate, Func<Error> errorFactory)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(predicate);
            ArgumentNullException.ThrowIfNull(errorFactory);

            return Core(source, predicate, errorFactory);

            static async Task<Result> Core(Task<Result> source, Func<bool> predicate, Func<Error> errorFactory) =>
                (await source.ConfigureAwait(false)).Ensure(predicate, errorFactory);
        }

        /// <summary>Awaits the result, then turns a success into a failure built by <paramref name="errorFactory"/> when the asynchronous <paramref name="predicate"/> is not satisfied.</summary>
        public Task<Result> EnsureAsync(Func<Task<bool>> predicate, Func<Error> errorFactory)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(predicate);
            ArgumentNullException.ThrowIfNull(errorFactory);

            return Core(source, predicate, errorFactory);

            static async Task<Result> Core(Task<Result> source, Func<Task<bool>> predicate, Func<Error> errorFactory) =>
                await (await source.ConfigureAwait(false)).EnsureAsync(predicate, errorFactory).ConfigureAwait(false);
        }

        /// <summary>Awaits the result, then transforms the error of a failure; successes are propagated unchanged.</summary>
        public Task<Result> MapErrorAsync(Func<Error, Error> mapper)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(mapper);

            return Core(source, mapper);

            static async Task<Result> Core(Task<Result> source, Func<Error, Error> mapper) =>
                (await source.ConfigureAwait(false)).MapError(mapper);
        }

        /// <summary>Awaits the result, then transforms the error of a failure with an asynchronous mapper; successes are propagated unchanged.</summary>
        public Task<Result> MapErrorAsync(Func<Error, Task<Error>> mapper)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(mapper);

            return Core(source, mapper);

            static async Task<Result> Core(Task<Result> source, Func<Error, Task<Error>> mapper) =>
                await (await source.ConfigureAwait(false)).MapErrorAsync(mapper).ConfigureAwait(false);
        }

        /// <summary>Awaits the result, then invokes a side effect when it is a success, and returns the result unchanged.</summary>
        public Task<Result> TapAsync(Action action)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(action);

            return Core(source, action);

            static async Task<Result> Core(Task<Result> source, Action action) =>
                (await source.ConfigureAwait(false)).Tap(action);
        }

        /// <summary>Awaits the result, then awaits an asynchronous side effect when it is a success, and returns the result unchanged.</summary>
        public Task<Result> TapAsync(Func<Task> action)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(action);

            return Core(source, action);

            static async Task<Result> Core(Task<Result> source, Func<Task> action) =>
                await (await source.ConfigureAwait(false)).TapAsync(action).ConfigureAwait(false);
        }

        /// <summary>Awaits the result, then invokes a side effect with the error of a failure, and returns the result unchanged.</summary>
        public Task<Result> TapErrorAsync(Action<Error> action)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(action);

            return Core(source, action);

            static async Task<Result> Core(Task<Result> source, Action<Error> action) =>
                (await source.ConfigureAwait(false)).TapError(action);
        }

        /// <summary>Awaits the result, then awaits an asynchronous side effect with the error of a failure, and returns the result unchanged.</summary>
        public Task<Result> TapErrorAsync(Func<Error, Task> action)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(action);

            return Core(source, action);

            static async Task<Result> Core(Task<Result> source, Func<Error, Task> action) =>
                await (await source.ConfigureAwait(false)).TapErrorAsync(action).ConfigureAwait(false);
        }

        /// <summary>Awaits the result, then projects it into a single value by invoking the branch that matches its state.</summary>
        public Task<TResult> MatchAsync<TResult>(Func<TResult> onSuccess, Func<Error, TResult> onFailure)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(onSuccess);
            ArgumentNullException.ThrowIfNull(onFailure);

            return Core(source, onSuccess, onFailure);

            static async Task<TResult> Core(Task<Result> source, Func<TResult> onSuccess, Func<Error, TResult> onFailure) =>
                (await source.ConfigureAwait(false)).Match(onSuccess, onFailure);
        }

        /// <summary>Awaits the result, then projects it into a single value by awaiting the asynchronous branch that matches its state.</summary>
        public Task<TResult> MatchAsync<TResult>(Func<Task<TResult>> onSuccess, Func<Error, Task<TResult>> onFailure)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(onSuccess);
            ArgumentNullException.ThrowIfNull(onFailure);

            return Core(source, onSuccess, onFailure);

            static async Task<TResult> Core(Task<Result> source, Func<Task<TResult>> onSuccess, Func<Error, Task<TResult>> onFailure) =>
                await (await source.ConfigureAwait(false)).MatchAsync(onSuccess, onFailure).ConfigureAwait(false);
        }

        /// <summary>Awaits the result, then invokes the branch that matches its state.</summary>
        public Task MatchAsync(Action onSuccess, Action<Error> onFailure)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(onSuccess);
            ArgumentNullException.ThrowIfNull(onFailure);

            return Core(source, onSuccess, onFailure);

            static async Task Core(Task<Result> source, Action onSuccess, Action<Error> onFailure) =>
                (await source.ConfigureAwait(false)).Match(onSuccess, onFailure);
        }

        /// <summary>Awaits the result, then awaits the asynchronous branch that matches its state.</summary>
        public Task MatchAsync(Func<Task> onSuccess, Func<Error, Task> onFailure)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(onSuccess);
            ArgumentNullException.ThrowIfNull(onFailure);

            return Core(source, onSuccess, onFailure);

            static async Task Core(Task<Result> source, Func<Task> onSuccess, Func<Error, Task> onFailure) =>
                await (await source.ConfigureAwait(false)).MatchAsync(onSuccess, onFailure).ConfigureAwait(false);
        }
    }
}
