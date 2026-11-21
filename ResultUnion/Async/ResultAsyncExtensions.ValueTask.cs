namespace ResultUnion;

// The same pipeline on ValueTask sources, for APIs that return ValueTask (EF Core's FindAsync, channels, your own hot
// paths). Generated from the Task-source methods: same names, delegates and behaviour. Each method awaits the ValueTask
// once, as a ValueTask must be, and returns a Task, so a chain continues on Task after its first step. Delegates stay
// Task-based on purpose: offering ValueTask delegates as well would make async lambdas ambiguous (DESIGN.md, 10).
public static partial class ResultAsyncExtensions
{
    /// <typeparam name="T">The type of the success value.</typeparam>
    /// <param name="source">The pending result to continue from. It is awaited once.</param>
    extension<T>(ValueTask<Result<T>> source)
        where T : notnull
    {
        /// <summary>Awaits the result, then transforms the value of a success; failures are propagated unchanged.</summary>
        public Task<Result<TResult>> MapAsync<TResult>(Func<T, TResult> mapper)
            where TResult : notnull
        {
            ArgumentNullException.ThrowIfNull(mapper);

            return Core(source, mapper);

            static async Task<Result<TResult>> Core(ValueTask<Result<T>> source, Func<T, TResult> mapper) =>
                (await source.ConfigureAwait(false)).Map(mapper);
        }

        /// <summary>Awaits the result, then transforms the value of a success with an asynchronous mapper; failures are propagated unchanged.</summary>
        public Task<Result<TResult>> MapAsync<TResult>(Func<T, Task<TResult>> mapper)
            where TResult : notnull
        {
            ArgumentNullException.ThrowIfNull(mapper);

            return Core(source, mapper);

            static async Task<Result<TResult>> Core(ValueTask<Result<T>> source, Func<T, Task<TResult>> mapper) =>
                await (await source.ConfigureAwait(false)).MapAsync(mapper).ConfigureAwait(false);
        }

        /// <summary>Awaits the result, then chains a value-producing operation that may fail; failures are propagated unchanged.</summary>
        public Task<Result<TResult>> BindAsync<TResult>(Func<T, Result<TResult>> next)
            where TResult : notnull
        {
            ArgumentNullException.ThrowIfNull(next);

            return Core(source, next);

            static async Task<Result<TResult>> Core(ValueTask<Result<T>> source, Func<T, Result<TResult>> next) =>
                (await source.ConfigureAwait(false)).Bind(next);
        }

        /// <summary>Awaits the result, then chains an asynchronous value-producing operation that may fail; failures are propagated unchanged.</summary>
        public Task<Result<TResult>> BindAsync<TResult>(Func<T, Task<Result<TResult>>> next)
            where TResult : notnull
        {
            ArgumentNullException.ThrowIfNull(next);

            return Core(source, next);

            static async Task<Result<TResult>> Core(ValueTask<Result<T>> source, Func<T, Task<Result<TResult>>> next) =>
                await (await source.ConfigureAwait(false)).BindAsync(next).ConfigureAwait(false);
        }

        /// <summary>Awaits the result, then chains an operation that may fail and produces no value; failures are propagated unchanged.</summary>
        public Task<Result> BindAsync(Func<T, Result> next)
        {
            ArgumentNullException.ThrowIfNull(next);

            return Core(source, next);

            static async Task<Result> Core(ValueTask<Result<T>> source, Func<T, Result> next) =>
                (await source.ConfigureAwait(false)).Bind(next);
        }

        /// <summary>Awaits the result, then chains an asynchronous operation that may fail and produces no value; failures are propagated unchanged.</summary>
        public Task<Result> BindAsync(Func<T, Task<Result>> next)
        {
            ArgumentNullException.ThrowIfNull(next);

            return Core(source, next);

            static async Task<Result> Core(ValueTask<Result<T>> source, Func<T, Task<Result>> next) =>
                await (await source.ConfigureAwait(false)).BindAsync(next).ConfigureAwait(false);
        }

        /// <summary>Awaits the result, then turns a success into a failure carrying <paramref name="error"/> when <paramref name="predicate"/> is not satisfied.</summary>
        public Task<Result<T>> EnsureAsync(Func<T, bool> predicate, Error error)
        {
            ArgumentNullException.ThrowIfNull(predicate);
            ArgumentNullException.ThrowIfNull(error);

            return Core(source, predicate, error);

            static async Task<Result<T>> Core(ValueTask<Result<T>> source, Func<T, bool> predicate, Error error) =>
                (await source.ConfigureAwait(false)).Ensure(predicate, error);
        }

        /// <summary>Awaits the result, then turns a success into a failure carrying <paramref name="error"/> when the asynchronous <paramref name="predicate"/> is not satisfied.</summary>
        public Task<Result<T>> EnsureAsync(Func<T, Task<bool>> predicate, Error error)
        {
            ArgumentNullException.ThrowIfNull(predicate);
            ArgumentNullException.ThrowIfNull(error);

            return Core(source, predicate, error);

            static async Task<Result<T>> Core(ValueTask<Result<T>> source, Func<T, Task<bool>> predicate, Error error) =>
                await (await source.ConfigureAwait(false)).EnsureAsync(predicate, error).ConfigureAwait(false);
        }

        /// <summary>Awaits the result, then turns a success into a failure built from the value when <paramref name="predicate"/> is not satisfied.</summary>
        public Task<Result<T>> EnsureAsync(Func<T, bool> predicate, Func<T, Error> errorFactory)
        {
            ArgumentNullException.ThrowIfNull(predicate);
            ArgumentNullException.ThrowIfNull(errorFactory);

            return Core(source, predicate, errorFactory);

            static async Task<Result<T>> Core(ValueTask<Result<T>> source, Func<T, bool> predicate, Func<T, Error> errorFactory) =>
                (await source.ConfigureAwait(false)).Ensure(predicate, errorFactory);
        }

        /// <summary>Awaits the result, then turns a success into a failure built from the value when the asynchronous <paramref name="predicate"/> is not satisfied.</summary>
        public Task<Result<T>> EnsureAsync(Func<T, Task<bool>> predicate, Func<T, Error> errorFactory)
        {
            ArgumentNullException.ThrowIfNull(predicate);
            ArgumentNullException.ThrowIfNull(errorFactory);

            return Core(source, predicate, errorFactory);

            static async Task<Result<T>> Core(ValueTask<Result<T>> source, Func<T, Task<bool>> predicate, Func<T, Error> errorFactory) =>
                await (await source.ConfigureAwait(false)).EnsureAsync(predicate, errorFactory).ConfigureAwait(false);
        }

        /// <summary>Awaits the result, then runs a validation that returns a <see cref="Result"/>; on success keeps the value, on failure returns its error.</summary>
        public Task<Result<T>> EnsureAsync(Func<T, Result> check)
        {
            ArgumentNullException.ThrowIfNull(check);

            return Core(source, check);

            static async Task<Result<T>> Core(ValueTask<Result<T>> source, Func<T, Result> check) =>
                (await source.ConfigureAwait(false)).Ensure(check);
        }

        /// <summary>Awaits the result, then runs an asynchronous validation that returns a <see cref="Result"/>; on success keeps the value, on failure returns its error.</summary>
        public Task<Result<T>> EnsureAsync(Func<T, Task<Result>> check)
        {
            ArgumentNullException.ThrowIfNull(check);

            return Core(source, check);

            static async Task<Result<T>> Core(ValueTask<Result<T>> source, Func<T, Task<Result>> check) =>
                await (await source.ConfigureAwait(false)).EnsureAsync(check).ConfigureAwait(false);
        }

        /// <summary>Awaits the result, then transforms the error of a failure; successes are propagated unchanged.</summary>
        public Task<Result<T>> MapErrorAsync(Func<Error, Error> mapper)
        {
            ArgumentNullException.ThrowIfNull(mapper);

            return Core(source, mapper);

            static async Task<Result<T>> Core(ValueTask<Result<T>> source, Func<Error, Error> mapper) =>
                (await source.ConfigureAwait(false)).MapError(mapper);
        }

        /// <summary>Awaits the result, then transforms the error of a failure with an asynchronous mapper; successes are propagated unchanged.</summary>
        public Task<Result<T>> MapErrorAsync(Func<Error, Task<Error>> mapper)
        {
            ArgumentNullException.ThrowIfNull(mapper);

            return Core(source, mapper);

            static async Task<Result<T>> Core(ValueTask<Result<T>> source, Func<Error, Task<Error>> mapper) =>
                await (await source.ConfigureAwait(false)).MapErrorAsync(mapper).ConfigureAwait(false);
        }

        /// <summary>Awaits the result, then invokes a side effect with the value of a success, and returns the result unchanged.</summary>
        public Task<Result<T>> TapAsync(Action<T> action)
        {
            ArgumentNullException.ThrowIfNull(action);

            return Core(source, action);

            static async Task<Result<T>> Core(ValueTask<Result<T>> source, Action<T> action) =>
                (await source.ConfigureAwait(false)).Tap(action);
        }

        /// <summary>Awaits the result, then awaits an asynchronous side effect with the value of a success, and returns the result unchanged.</summary>
        public Task<Result<T>> TapAsync(Func<T, Task> action)
        {
            ArgumentNullException.ThrowIfNull(action);

            return Core(source, action);

            static async Task<Result<T>> Core(ValueTask<Result<T>> source, Func<T, Task> action) =>
                await (await source.ConfigureAwait(false)).TapAsync(action).ConfigureAwait(false);
        }

        /// <summary>Awaits the result, then invokes a side effect with the error of a failure, and returns the result unchanged.</summary>
        public Task<Result<T>> TapErrorAsync(Action<Error> action)
        {
            ArgumentNullException.ThrowIfNull(action);

            return Core(source, action);

            static async Task<Result<T>> Core(ValueTask<Result<T>> source, Action<Error> action) =>
                (await source.ConfigureAwait(false)).TapError(action);
        }

        /// <summary>Awaits the result, then awaits an asynchronous side effect with the error of a failure, and returns the result unchanged.</summary>
        public Task<Result<T>> TapErrorAsync(Func<Error, Task> action)
        {
            ArgumentNullException.ThrowIfNull(action);

            return Core(source, action);

            static async Task<Result<T>> Core(ValueTask<Result<T>> source, Func<Error, Task> action) =>
                await (await source.ConfigureAwait(false)).TapErrorAsync(action).ConfigureAwait(false);
        }

        /// <summary>Awaits the result, then projects it into a single value by invoking the branch that matches its state.</summary>
        public Task<TResult> MatchAsync<TResult>(Func<T, TResult> onSuccess, Func<Error, TResult> onFailure)
        {
            ArgumentNullException.ThrowIfNull(onSuccess);
            ArgumentNullException.ThrowIfNull(onFailure);

            return Core(source, onSuccess, onFailure);

            static async Task<TResult> Core(ValueTask<Result<T>> source, Func<T, TResult> onSuccess, Func<Error, TResult> onFailure) =>
                (await source.ConfigureAwait(false)).Match(onSuccess, onFailure);
        }

        /// <summary>Awaits the result, then projects it into a single value by awaiting the asynchronous branch that matches its state.</summary>
        public Task<TResult> MatchAsync<TResult>(Func<T, Task<TResult>> onSuccess, Func<Error, Task<TResult>> onFailure)
        {
            ArgumentNullException.ThrowIfNull(onSuccess);
            ArgumentNullException.ThrowIfNull(onFailure);

            return Core(source, onSuccess, onFailure);

            static async Task<TResult> Core(ValueTask<Result<T>> source, Func<T, Task<TResult>> onSuccess, Func<Error, Task<TResult>> onFailure) =>
                await (await source.ConfigureAwait(false)).MatchAsync(onSuccess, onFailure).ConfigureAwait(false);
        }

        /// <summary>Awaits the result, then invokes the branch that matches its state.</summary>
        public Task MatchAsync(Action<T> onSuccess, Action<Error> onFailure)
        {
            ArgumentNullException.ThrowIfNull(onSuccess);
            ArgumentNullException.ThrowIfNull(onFailure);

            return Core(source, onSuccess, onFailure);

            static async Task Core(ValueTask<Result<T>> source, Action<T> onSuccess, Action<Error> onFailure) =>
                (await source.ConfigureAwait(false)).Match(onSuccess, onFailure);
        }

        /// <summary>Awaits the result, then awaits the asynchronous branch that matches its state.</summary>
        public Task MatchAsync(Func<T, Task> onSuccess, Func<Error, Task> onFailure)
        {
            ArgumentNullException.ThrowIfNull(onSuccess);
            ArgumentNullException.ThrowIfNull(onFailure);

            return Core(source, onSuccess, onFailure);

            static async Task Core(ValueTask<Result<T>> source, Func<T, Task> onSuccess, Func<Error, Task> onFailure) =>
                await (await source.ConfigureAwait(false)).MatchAsync(onSuccess, onFailure).ConfigureAwait(false);
        }
    }

    /// <param name="source">The pending result to continue from. It is awaited once.</param>
    extension(ValueTask<Result> source)
    {
        /// <summary>Awaits the result, then chains an operation that may fail and produces no value; failures are propagated unchanged.</summary>
        public Task<Result> BindAsync(Func<Result> next)
        {
            ArgumentNullException.ThrowIfNull(next);

            return Core(source, next);

            static async Task<Result> Core(ValueTask<Result> source, Func<Result> next) =>
                (await source.ConfigureAwait(false)).Bind(next);
        }

        /// <summary>Awaits the result, then chains an asynchronous operation that may fail and produces no value; failures are propagated unchanged.</summary>
        public Task<Result> BindAsync(Func<Task<Result>> next)
        {
            ArgumentNullException.ThrowIfNull(next);

            return Core(source, next);

            static async Task<Result> Core(ValueTask<Result> source, Func<Task<Result>> next) =>
                await (await source.ConfigureAwait(false)).BindAsync(next).ConfigureAwait(false);
        }

        /// <summary>Awaits the result, then chains a value-producing operation that may fail; failures are propagated unchanged.</summary>
        public Task<Result<T>> BindAsync<T>(Func<Result<T>> next)
            where T : notnull
        {
            ArgumentNullException.ThrowIfNull(next);

            return Core(source, next);

            static async Task<Result<T>> Core(ValueTask<Result> source, Func<Result<T>> next) =>
                (await source.ConfigureAwait(false)).Bind(next);
        }

        /// <summary>Awaits the result, then chains an asynchronous value-producing operation that may fail; failures are propagated unchanged.</summary>
        public Task<Result<T>> BindAsync<T>(Func<Task<Result<T>>> next)
            where T : notnull
        {
            ArgumentNullException.ThrowIfNull(next);

            return Core(source, next);

            static async Task<Result<T>> Core(ValueTask<Result> source, Func<Task<Result<T>>> next) =>
                await (await source.ConfigureAwait(false)).BindAsync(next).ConfigureAwait(false);
        }

        /// <summary>Awaits the result, then turns a success into a failure carrying <paramref name="error"/> when <paramref name="predicate"/> is not satisfied.</summary>
        public Task<Result> EnsureAsync(Func<bool> predicate, Error error)
        {
            ArgumentNullException.ThrowIfNull(predicate);
            ArgumentNullException.ThrowIfNull(error);

            return Core(source, predicate, error);

            static async Task<Result> Core(ValueTask<Result> source, Func<bool> predicate, Error error) =>
                (await source.ConfigureAwait(false)).Ensure(predicate, error);
        }

        /// <summary>Awaits the result, then turns a success into a failure carrying <paramref name="error"/> when the asynchronous <paramref name="predicate"/> is not satisfied.</summary>
        public Task<Result> EnsureAsync(Func<Task<bool>> predicate, Error error)
        {
            ArgumentNullException.ThrowIfNull(predicate);
            ArgumentNullException.ThrowIfNull(error);

            return Core(source, predicate, error);

            static async Task<Result> Core(ValueTask<Result> source, Func<Task<bool>> predicate, Error error) =>
                await (await source.ConfigureAwait(false)).EnsureAsync(predicate, error).ConfigureAwait(false);
        }

        /// <summary>Awaits the result, then turns a success into a failure built by <paramref name="errorFactory"/> when <paramref name="predicate"/> is not satisfied.</summary>
        public Task<Result> EnsureAsync(Func<bool> predicate, Func<Error> errorFactory)
        {
            ArgumentNullException.ThrowIfNull(predicate);
            ArgumentNullException.ThrowIfNull(errorFactory);

            return Core(source, predicate, errorFactory);

            static async Task<Result> Core(ValueTask<Result> source, Func<bool> predicate, Func<Error> errorFactory) =>
                (await source.ConfigureAwait(false)).Ensure(predicate, errorFactory);
        }

        /// <summary>Awaits the result, then turns a success into a failure built by <paramref name="errorFactory"/> when the asynchronous <paramref name="predicate"/> is not satisfied.</summary>
        public Task<Result> EnsureAsync(Func<Task<bool>> predicate, Func<Error> errorFactory)
        {
            ArgumentNullException.ThrowIfNull(predicate);
            ArgumentNullException.ThrowIfNull(errorFactory);

            return Core(source, predicate, errorFactory);

            static async Task<Result> Core(ValueTask<Result> source, Func<Task<bool>> predicate, Func<Error> errorFactory) =>
                await (await source.ConfigureAwait(false)).EnsureAsync(predicate, errorFactory).ConfigureAwait(false);
        }

        /// <summary>Awaits the result, then transforms the error of a failure; successes are propagated unchanged.</summary>
        public Task<Result> MapErrorAsync(Func<Error, Error> mapper)
        {
            ArgumentNullException.ThrowIfNull(mapper);

            return Core(source, mapper);

            static async Task<Result> Core(ValueTask<Result> source, Func<Error, Error> mapper) =>
                (await source.ConfigureAwait(false)).MapError(mapper);
        }

        /// <summary>Awaits the result, then transforms the error of a failure with an asynchronous mapper; successes are propagated unchanged.</summary>
        public Task<Result> MapErrorAsync(Func<Error, Task<Error>> mapper)
        {
            ArgumentNullException.ThrowIfNull(mapper);

            return Core(source, mapper);

            static async Task<Result> Core(ValueTask<Result> source, Func<Error, Task<Error>> mapper) =>
                await (await source.ConfigureAwait(false)).MapErrorAsync(mapper).ConfigureAwait(false);
        }

        /// <summary>Awaits the result, then invokes a side effect when it is a success, and returns the result unchanged.</summary>
        public Task<Result> TapAsync(Action action)
        {
            ArgumentNullException.ThrowIfNull(action);

            return Core(source, action);

            static async Task<Result> Core(ValueTask<Result> source, Action action) =>
                (await source.ConfigureAwait(false)).Tap(action);
        }

        /// <summary>Awaits the result, then awaits an asynchronous side effect when it is a success, and returns the result unchanged.</summary>
        public Task<Result> TapAsync(Func<Task> action)
        {
            ArgumentNullException.ThrowIfNull(action);

            return Core(source, action);

            static async Task<Result> Core(ValueTask<Result> source, Func<Task> action) =>
                await (await source.ConfigureAwait(false)).TapAsync(action).ConfigureAwait(false);
        }

        /// <summary>Awaits the result, then invokes a side effect with the error of a failure, and returns the result unchanged.</summary>
        public Task<Result> TapErrorAsync(Action<Error> action)
        {
            ArgumentNullException.ThrowIfNull(action);

            return Core(source, action);

            static async Task<Result> Core(ValueTask<Result> source, Action<Error> action) =>
                (await source.ConfigureAwait(false)).TapError(action);
        }

        /// <summary>Awaits the result, then awaits an asynchronous side effect with the error of a failure, and returns the result unchanged.</summary>
        public Task<Result> TapErrorAsync(Func<Error, Task> action)
        {
            ArgumentNullException.ThrowIfNull(action);

            return Core(source, action);

            static async Task<Result> Core(ValueTask<Result> source, Func<Error, Task> action) =>
                await (await source.ConfigureAwait(false)).TapErrorAsync(action).ConfigureAwait(false);
        }

        /// <summary>Awaits the result, then projects it into a single value by invoking the branch that matches its state.</summary>
        public Task<TResult> MatchAsync<TResult>(Func<TResult> onSuccess, Func<Error, TResult> onFailure)
        {
            ArgumentNullException.ThrowIfNull(onSuccess);
            ArgumentNullException.ThrowIfNull(onFailure);

            return Core(source, onSuccess, onFailure);

            static async Task<TResult> Core(ValueTask<Result> source, Func<TResult> onSuccess, Func<Error, TResult> onFailure) =>
                (await source.ConfigureAwait(false)).Match(onSuccess, onFailure);
        }

        /// <summary>Awaits the result, then projects it into a single value by awaiting the asynchronous branch that matches its state.</summary>
        public Task<TResult> MatchAsync<TResult>(Func<Task<TResult>> onSuccess, Func<Error, Task<TResult>> onFailure)
        {
            ArgumentNullException.ThrowIfNull(onSuccess);
            ArgumentNullException.ThrowIfNull(onFailure);

            return Core(source, onSuccess, onFailure);

            static async Task<TResult> Core(ValueTask<Result> source, Func<Task<TResult>> onSuccess, Func<Error, Task<TResult>> onFailure) =>
                await (await source.ConfigureAwait(false)).MatchAsync(onSuccess, onFailure).ConfigureAwait(false);
        }

        /// <summary>Awaits the result, then invokes the branch that matches its state.</summary>
        public Task MatchAsync(Action onSuccess, Action<Error> onFailure)
        {
            ArgumentNullException.ThrowIfNull(onSuccess);
            ArgumentNullException.ThrowIfNull(onFailure);

            return Core(source, onSuccess, onFailure);

            static async Task Core(ValueTask<Result> source, Action onSuccess, Action<Error> onFailure) =>
                (await source.ConfigureAwait(false)).Match(onSuccess, onFailure);
        }

        /// <summary>Awaits the result, then awaits the asynchronous branch that matches its state.</summary>
        public Task MatchAsync(Func<Task> onSuccess, Func<Error, Task> onFailure)
        {
            ArgumentNullException.ThrowIfNull(onSuccess);
            ArgumentNullException.ThrowIfNull(onFailure);

            return Core(source, onSuccess, onFailure);

            static async Task Core(ValueTask<Result> source, Func<Task> onSuccess, Func<Error, Task> onFailure) =>
                await (await source.ConfigureAwait(false)).MatchAsync(onSuccess, onFailure).ConfigureAwait(false);
        }
    }
}
