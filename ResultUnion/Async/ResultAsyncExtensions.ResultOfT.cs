namespace ResultUnion;

/// <summary>Asynchronous counterparts of the <see cref="Result{T}"/> and <see cref="Result"/> operations.</summary>
/// <remarks>
/// <para>There are two groups of methods:</para>
/// <list type="bullet">
///   <item><description>on <see cref="Result{T}"/>, taking an asynchronous delegate, for an asynchronous step in the middle of a pipeline;</description></item>
///   <item><description>on <see cref="Task{TResult}"/> of <see cref="Result{T}"/>, taking a synchronous or an asynchronous delegate, so a pipeline continues after <see langword="await"/>.</description></item>
/// </list>
/// <para>
/// Arguments are validated eagerly: a <see langword="null"/> source or delegate throws <see cref="ArgumentNullException"/>
/// when the method is called, not when the returned task is awaited. Exceptions thrown by the delegates themselves surface
/// through the returned task.
/// </para>
/// <para>
/// Delegates are not invoked on the track they do not apply to, exactly like their synchronous counterparts.
/// A <see cref="CancellationToken"/> is passed to the delegates by closure, e.g. <c>user => orders.CreateAsync(user, cancellationToken)</c>.
/// </para>
/// </remarks>
public static partial class ResultAsyncExtensions
{
    /// <typeparam name="T">The type of the success value.</typeparam>
    /// <param name="result">The result to continue from.</param>
    extension<T>(Result<T> result)
        where T : notnull
    {
        /// <summary>Transforms the value of a successful result with an asynchronous mapper; failures are propagated unchanged.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="mapper"/> is <see langword="null"/>. Through the task: the mapper returned <see langword="null"/>.</exception>
        public Task<Result<TResult>> MapAsync<TResult>(Func<T, Task<TResult>> mapper)
            where TResult : notnull
        {
            ArgumentNullException.ThrowIfNull(mapper);

            if (!result.TryGetResultValue(out var value))
            {
                return Task.FromResult(Result<TResult>.Failure(result.Error));
            }

            return Core(value, mapper);

            static async Task<Result<TResult>> Core(T value, Func<T, Task<TResult>> mapper) =>
                Result<TResult>.Success(await mapper(value).ConfigureAwait(false));
        }

        /// <summary>Chains an asynchronous value-producing operation that may fail, when this result is a success; failures are propagated unchanged.</summary>
        public Task<Result<TResult>> BindAsync<TResult>(Func<T, Task<Result<TResult>>> next)
            where TResult : notnull
        {
            ArgumentNullException.ThrowIfNull(next);

            if (!result.TryGetResultValue(out var value))
            {
                return Task.FromResult(Result<TResult>.Failure(result.Error));
            }

            return Invoke(next, value);
        }

        /// <summary>Chains an asynchronous operation that may fail and produces no value, when this result is a success; failures are propagated unchanged.</summary>
        public Task<Result> BindAsync(Func<T, Task<Result>> next)
        {
            ArgumentNullException.ThrowIfNull(next);

            if (!result.TryGetResultValue(out var value))
            {
                return Task.FromResult(Result.Failure(result.Error));
            }

            return Invoke(next, value);
        }

        /// <summary>Turns a successful result into a failure carrying <paramref name="error"/> when the asynchronous <paramref name="predicate"/> is not satisfied.</summary>
        /// <remarks>Failures are propagated unchanged and <paramref name="predicate"/> is not invoked.</remarks>
        public Task<Result<T>> EnsureAsync(Func<T, Task<bool>> predicate, Error error)
        {
            ArgumentNullException.ThrowIfNull(predicate);
            ArgumentNullException.ThrowIfNull(error);

            if (!result.TryGetResultValue(out var value))
            {
                return Task.FromResult(result);
            }

            return Core(result, value, predicate, error);

            static async Task<Result<T>> Core(Result<T> result, T value, Func<T, Task<bool>> predicate, Error error) =>
                await predicate(value).ConfigureAwait(false)
                    ? result
                    : Result<T>.Failure(error);
        }

        /// <summary>Turns a successful result into a failure built from the value by <paramref name="errorFactory"/> when the asynchronous <paramref name="predicate"/> is not satisfied.</summary>
        /// <remarks>Failures are propagated unchanged and neither delegate is invoked; <paramref name="errorFactory"/> is invoked only when <paramref name="predicate"/> fails.</remarks>
        public Task<Result<T>> EnsureAsync(Func<T, Task<bool>> predicate, Func<T, Error> errorFactory)
        {
            ArgumentNullException.ThrowIfNull(predicate);
            ArgumentNullException.ThrowIfNull(errorFactory);

            if (!result.TryGetResultValue(out var value))
            {
                return Task.FromResult(result);
            }

            return Core(result, value, predicate, errorFactory);

            static async Task<Result<T>> Core(Result<T> result, T value, Func<T, Task<bool>> predicate, Func<T, Error> errorFactory) =>
                await predicate(value).ConfigureAwait(false)
                    ? result
                    : Result<T>.Failure(errorFactory(value));
        }

        /// <summary>Runs an asynchronous validation that returns a <see cref="Result"/>; on success keeps the value, on failure returns its error.</summary>
        /// <remarks>Failures are propagated unchanged and <paramref name="check"/> is not invoked.</remarks>
        public Task<Result<T>> EnsureAsync(Func<T, Task<Result>> check)
        {
            ArgumentNullException.ThrowIfNull(check);

            if (!result.TryGetResultValue(out var value))
            {
                return Task.FromResult(result);
            }

            return Core(result, value, check);

            static async Task<Result<T>> Core(Result<T> result, T value, Func<T, Task<Result>> check)
            {
                var outcome = await check(value).ConfigureAwait(false);

                return outcome.IsSuccess
                    ? result
                    : Result<T>.Failure(outcome.Error);
            }
        }

        /// <summary>Transforms the error of a failed result with an asynchronous mapper; successes are propagated unchanged.</summary>
        public Task<Result<T>> MapErrorAsync(Func<Error, Task<Error>> mapper)
        {
            ArgumentNullException.ThrowIfNull(mapper);

            if (!result.TryGetError(out var error))
            {
                return Task.FromResult(result);
            }

            return Core(error, mapper);

            static async Task<Result<T>> Core(Error error, Func<Error, Task<Error>> mapper) =>
                Result<T>.Failure(await mapper(error).ConfigureAwait(false));
        }

        /// <summary>Awaits an asynchronous side effect with the value when the result is a success, then returns the result unchanged.</summary>
        public Task<Result<T>> TapAsync(Func<T, Task> action)
        {
            ArgumentNullException.ThrowIfNull(action);

            if (!result.TryGetResultValue(out var value))
            {
                return Task.FromResult(result);
            }

            return Core(result, value, action);

            static async Task<Result<T>> Core(Result<T> result, T value, Func<T, Task> action)
            {
                await action(value).ConfigureAwait(false);

                return result;
            }
        }

        /// <summary>Awaits an asynchronous side effect with the error when the result is a failure, then returns the result unchanged.</summary>
        public Task<Result<T>> TapErrorAsync(Func<Error, Task> action)
        {
            ArgumentNullException.ThrowIfNull(action);

            if (!result.TryGetError(out var error))
            {
                return Task.FromResult(result);
            }

            return Core(result, error, action);

            static async Task<Result<T>> Core(Result<T> result, Error error, Func<Error, Task> action)
            {
                await action(error).ConfigureAwait(false);

                return result;
            }
        }

        /// <summary>Projects the result into a single value by awaiting the asynchronous branch that matches its state.</summary>
        public Task<TResult> MatchAsync<TResult>(Func<T, Task<TResult>> onSuccess, Func<Error, Task<TResult>> onFailure)
        {
            ArgumentNullException.ThrowIfNull(onSuccess);
            ArgumentNullException.ThrowIfNull(onFailure);

            return result.TryGetResultValue(out var value)
                ? Invoke(onSuccess, value)
                : Invoke(onFailure, result.Error);
        }

        /// <summary>Awaits the asynchronous branch that matches the state of the result.</summary>
        public Task MatchAsync(Func<T, Task> onSuccess, Func<Error, Task> onFailure)
        {
            ArgumentNullException.ThrowIfNull(onSuccess);
            ArgumentNullException.ThrowIfNull(onFailure);

            return result.TryGetResultValue(out var value)
                ? InvokeAction(onSuccess, value)
                : InvokeAction(onFailure, result.Error);
        }
    }

    /// <typeparam name="T">The type of the success value.</typeparam>
    /// <param name="source">The pending result to continue from.</param>
    extension<T>(Task<Result<T>> source)
        where T : notnull
    {
        /// <summary>Awaits the result, then transforms the value of a success; failures are propagated unchanged.</summary>
        public Task<Result<TResult>> MapAsync<TResult>(Func<T, TResult> mapper)
            where TResult : notnull
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(mapper);

            return Core(source, mapper);

            static async Task<Result<TResult>> Core(Task<Result<T>> source, Func<T, TResult> mapper) =>
                (await source.ConfigureAwait(false)).Map(mapper);
        }

        /// <summary>Awaits the result, then transforms the value of a success with an asynchronous mapper; failures are propagated unchanged.</summary>
        public Task<Result<TResult>> MapAsync<TResult>(Func<T, Task<TResult>> mapper)
            where TResult : notnull
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(mapper);

            if (source.IsCompletedSuccessfully)
            {
                return source.Result.MapAsync(mapper);
            }

            return Core(source, mapper);

            static async Task<Result<TResult>> Core(Task<Result<T>> source, Func<T, Task<TResult>> mapper) =>
                await (await source.ConfigureAwait(false)).MapAsync(mapper).ConfigureAwait(false);
        }

        /// <summary>Awaits the result, then chains a value-producing operation that may fail; failures are propagated unchanged.</summary>
        public Task<Result<TResult>> BindAsync<TResult>(Func<T, Result<TResult>> next)
            where TResult : notnull
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(next);

            return Core(source, next);

            static async Task<Result<TResult>> Core(Task<Result<T>> source, Func<T, Result<TResult>> next) =>
                (await source.ConfigureAwait(false)).Bind(next);
        }

        /// <summary>Awaits the result, then chains an asynchronous value-producing operation that may fail; failures are propagated unchanged.</summary>
        public Task<Result<TResult>> BindAsync<TResult>(Func<T, Task<Result<TResult>>> next)
            where TResult : notnull
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(next);

            if (source.IsCompletedSuccessfully)
            {
                return source.Result.BindAsync(next);
            }

            return Core(source, next);

            static async Task<Result<TResult>> Core(Task<Result<T>> source, Func<T, Task<Result<TResult>>> next) =>
                await (await source.ConfigureAwait(false)).BindAsync(next).ConfigureAwait(false);
        }

        /// <summary>Awaits the result, then chains an operation that may fail and produces no value; failures are propagated unchanged.</summary>
        public Task<Result> BindAsync(Func<T, Result> next)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(next);

            if (source.IsCompletedSuccessfully)
            {
                return RunCompletedResult(source.Result, next, static (r, n) => r.Bind(n));
            }

            return Core(source, next);

            static async Task<Result> Core(Task<Result<T>> source, Func<T, Result> next) =>
                (await source.ConfigureAwait(false)).Bind(next);
        }

        /// <summary>Awaits the result, then chains an asynchronous operation that may fail and produces no value; failures are propagated unchanged.</summary>
        public Task<Result> BindAsync(Func<T, Task<Result>> next)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(next);

            if (source.IsCompletedSuccessfully)
            {
                return source.Result.BindAsync(next);
            }

            return Core(source, next);

            static async Task<Result> Core(Task<Result<T>> source, Func<T, Task<Result>> next) =>
                await (await source.ConfigureAwait(false)).BindAsync(next).ConfigureAwait(false);
        }

        /// <summary>Awaits the result, then turns a success into a failure carrying <paramref name="error"/> when <paramref name="predicate"/> is not satisfied.</summary>
        public Task<Result<T>> EnsureAsync(Func<T, bool> predicate, Error error)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(predicate);
            ArgumentNullException.ThrowIfNull(error);

            if (source.IsCompletedSuccessfully)
            {
                return KeepCompleted(source, (predicate, error), static (r, s) => r.Ensure(s.predicate, s.error));
            }

            return Core(source, predicate, error);

            static async Task<Result<T>> Core(Task<Result<T>> source, Func<T, bool> predicate, Error error) =>
                (await source.ConfigureAwait(false)).Ensure(predicate, error);
        }

        /// <summary>Awaits the result, then turns a success into a failure carrying <paramref name="error"/> when the asynchronous <paramref name="predicate"/> is not satisfied.</summary>
        public Task<Result<T>> EnsureAsync(Func<T, Task<bool>> predicate, Error error)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(predicate);
            ArgumentNullException.ThrowIfNull(error);

            if (source.IsCompletedSuccessfully)
            {
                return source.Result.EnsureAsync(predicate, error);
            }

            return Core(source, predicate, error);

            static async Task<Result<T>> Core(Task<Result<T>> source, Func<T, Task<bool>> predicate, Error error) =>
                await (await source.ConfigureAwait(false)).EnsureAsync(predicate, error).ConfigureAwait(false);
        }

        /// <summary>Awaits the result, then turns a success into a failure built from the value when <paramref name="predicate"/> is not satisfied.</summary>
        public Task<Result<T>> EnsureAsync(Func<T, bool> predicate, Func<T, Error> errorFactory)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(predicate);
            ArgumentNullException.ThrowIfNull(errorFactory);

            if (source.IsCompletedSuccessfully)
            {
                return KeepCompleted(source, (predicate, errorFactory), static (r, s) => r.Ensure(s.predicate, s.errorFactory));
            }

            return Core(source, predicate, errorFactory);

            static async Task<Result<T>> Core(Task<Result<T>> source, Func<T, bool> predicate, Func<T, Error> errorFactory) =>
                (await source.ConfigureAwait(false)).Ensure(predicate, errorFactory);
        }

        /// <summary>Awaits the result, then turns a success into a failure built from the value when the asynchronous <paramref name="predicate"/> is not satisfied.</summary>
        public Task<Result<T>> EnsureAsync(Func<T, Task<bool>> predicate, Func<T, Error> errorFactory)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(predicate);
            ArgumentNullException.ThrowIfNull(errorFactory);

            if (source.IsCompletedSuccessfully)
            {
                return source.Result.EnsureAsync(predicate, errorFactory);
            }

            return Core(source, predicate, errorFactory);

            static async Task<Result<T>> Core(Task<Result<T>> source, Func<T, Task<bool>> predicate, Func<T, Error> errorFactory) =>
                await (await source.ConfigureAwait(false)).EnsureAsync(predicate, errorFactory).ConfigureAwait(false);
        }

        /// <summary>Awaits the result, then runs a validation that returns a <see cref="Result"/>; on success keeps the value, on failure returns its error.</summary>
        public Task<Result<T>> EnsureAsync(Func<T, Result> check)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(check);

            if (source.IsCompletedSuccessfully)
            {
                return KeepCompleted(source, check, static (r, c) => r.Ensure(c));
            }

            return Core(source, check);

            static async Task<Result<T>> Core(Task<Result<T>> source, Func<T, Result> check) =>
                (await source.ConfigureAwait(false)).Ensure(check);
        }

        /// <summary>Awaits the result, then runs an asynchronous validation that returns a <see cref="Result"/>; on success keeps the value, on failure returns its error.</summary>
        public Task<Result<T>> EnsureAsync(Func<T, Task<Result>> check)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(check);

            if (source.IsCompletedSuccessfully)
            {
                return source.Result.EnsureAsync(check);
            }

            return Core(source, check);

            static async Task<Result<T>> Core(Task<Result<T>> source, Func<T, Task<Result>> check) =>
                await (await source.ConfigureAwait(false)).EnsureAsync(check).ConfigureAwait(false);
        }

        /// <summary>Awaits the result, then transforms the error of a failure; successes are propagated unchanged.</summary>
        public Task<Result<T>> MapErrorAsync(Func<Error, Error> mapper)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(mapper);

            if (source.IsCompletedSuccessfully)
            {
                return KeepCompleted(source, mapper, static (r, m) => r.MapError(m));
            }

            return Core(source, mapper);

            static async Task<Result<T>> Core(Task<Result<T>> source, Func<Error, Error> mapper) =>
                (await source.ConfigureAwait(false)).MapError(mapper);
        }

        /// <summary>Awaits the result, then transforms the error of a failure with an asynchronous mapper; successes are propagated unchanged.</summary>
        public Task<Result<T>> MapErrorAsync(Func<Error, Task<Error>> mapper)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(mapper);

            if (source.IsCompletedSuccessfully)
            {
                return source.Result.MapErrorAsync(mapper);
            }

            return Core(source, mapper);

            static async Task<Result<T>> Core(Task<Result<T>> source, Func<Error, Task<Error>> mapper) =>
                await (await source.ConfigureAwait(false)).MapErrorAsync(mapper).ConfigureAwait(false);
        }

        /// <summary>Awaits the result, then invokes a side effect with the value of a success, and returns the result unchanged.</summary>
        public Task<Result<T>> TapAsync(Action<T> action)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(action);

            if (source.IsCompletedSuccessfully)
            {
                return KeepCompleted(source, action, static (r, a) => r.Tap(a));
            }

            return Core(source, action);

            static async Task<Result<T>> Core(Task<Result<T>> source, Action<T> action) =>
                (await source.ConfigureAwait(false)).Tap(action);
        }

        /// <summary>Awaits the result, then awaits an asynchronous side effect with the value of a success, and returns the result unchanged.</summary>
        public Task<Result<T>> TapAsync(Func<T, Task> action)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(action);

            if (source.IsCompletedSuccessfully)
            {
                return source.Result.TapAsync(action);
            }

            return Core(source, action);

            static async Task<Result<T>> Core(Task<Result<T>> source, Func<T, Task> action) =>
                await (await source.ConfigureAwait(false)).TapAsync(action).ConfigureAwait(false);
        }

        /// <summary>Awaits the result, then invokes a side effect with the error of a failure, and returns the result unchanged.</summary>
        public Task<Result<T>> TapErrorAsync(Action<Error> action)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(action);

            if (source.IsCompletedSuccessfully)
            {
                return KeepCompleted(source, action, static (r, a) => r.TapError(a));
            }

            return Core(source, action);

            static async Task<Result<T>> Core(Task<Result<T>> source, Action<Error> action) =>
                (await source.ConfigureAwait(false)).TapError(action);
        }

        /// <summary>Awaits the result, then awaits an asynchronous side effect with the error of a failure, and returns the result unchanged.</summary>
        public Task<Result<T>> TapErrorAsync(Func<Error, Task> action)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(action);

            if (source.IsCompletedSuccessfully)
            {
                return source.Result.TapErrorAsync(action);
            }

            return Core(source, action);

            static async Task<Result<T>> Core(Task<Result<T>> source, Func<Error, Task> action) =>
                await (await source.ConfigureAwait(false)).TapErrorAsync(action).ConfigureAwait(false);
        }

        /// <summary>Awaits the result, then projects it into a single value by invoking the branch that matches its state.</summary>
        public Task<TResult> MatchAsync<TResult>(Func<T, TResult> onSuccess, Func<Error, TResult> onFailure)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(onSuccess);
            ArgumentNullException.ThrowIfNull(onFailure);

            return Core(source, onSuccess, onFailure);

            static async Task<TResult> Core(Task<Result<T>> source, Func<T, TResult> onSuccess, Func<Error, TResult> onFailure) =>
                (await source.ConfigureAwait(false)).Match(onSuccess, onFailure);
        }

        /// <summary>Awaits the result, then projects it into a single value by awaiting the asynchronous branch that matches its state.</summary>
        public Task<TResult> MatchAsync<TResult>(Func<T, Task<TResult>> onSuccess, Func<Error, Task<TResult>> onFailure)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(onSuccess);
            ArgumentNullException.ThrowIfNull(onFailure);

            if (source.IsCompletedSuccessfully)
            {
                return source.Result.MatchAsync(onSuccess, onFailure);
            }

            return Core(source, onSuccess, onFailure);

            static async Task<TResult> Core(Task<Result<T>> source, Func<T, Task<TResult>> onSuccess, Func<Error, Task<TResult>> onFailure) =>
                await (await source.ConfigureAwait(false)).MatchAsync(onSuccess, onFailure).ConfigureAwait(false);
        }

        /// <summary>Awaits the result, then invokes the branch that matches its state.</summary>
        public Task MatchAsync(Action<T> onSuccess, Action<Error> onFailure)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(onSuccess);
            ArgumentNullException.ThrowIfNull(onFailure);

            return Core(source, onSuccess, onFailure);

            static async Task Core(Task<Result<T>> source, Action<T> onSuccess, Action<Error> onFailure) =>
                (await source.ConfigureAwait(false)).Match(onSuccess, onFailure);
        }

        /// <summary>Awaits the result, then awaits the asynchronous branch that matches its state.</summary>
        public Task MatchAsync(Func<T, Task> onSuccess, Func<Error, Task> onFailure)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(onSuccess);
            ArgumentNullException.ThrowIfNull(onFailure);

            if (source.IsCompletedSuccessfully)
            {
                return source.Result.MatchAsync(onSuccess, onFailure);
            }

            return Core(source, onSuccess, onFailure);

            static async Task Core(Task<Result<T>> source, Func<T, Task> onSuccess, Func<Error, Task> onFailure) =>
                await (await source.ConfigureAwait(false)).MatchAsync(onSuccess, onFailure).ConfigureAwait(false);
        }
    }
}
