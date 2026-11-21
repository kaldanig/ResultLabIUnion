using ResultUnion.Diagnostics;

namespace ResultUnion;

/// <summary>
/// <c>Record</c> and <c>RecordAsync</c>: report a failure to OpenTelemetry through <see cref="ResultTelemetry.Record"/>
/// and return the result unchanged, so they fit at the end of a pipeline:
/// <c>.TapAsync(user => users.AddAsync(user, ct)).RecordAsync("users.register")</c>.
/// </summary>
/// <remarks>
/// A success records nothing. The operation name becomes a metric attribute, so it must be a stable, low-cardinality
/// name such as <c>users.register</c>, never an id or user input. Arguments are validated when the method is called.
/// </remarks>
public static class ResultTelemetryExtensions
{
    /// <param name="result">The result to report.</param>
    extension(Result result)
    {
        /// <summary>Reports a failure of <paramref name="operation"/> and returns the result unchanged.</summary>
        /// <exception cref="ArgumentException"><paramref name="operation"/> is <see langword="null"/>, empty or white space.</exception>
        public Result Record(string operation)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(operation);

            if (result.TryGetError(out var error))
            {
                ResultTelemetry.Record(error, operation);
            }

            return result;
        }
    }

    /// <typeparam name="T">The type of the success value.</typeparam>
    /// <param name="result">The result to report.</param>
    extension<T>(Result<T> result)
        where T : notnull
    {
        /// <summary>Reports a failure of <paramref name="operation"/> and returns the result unchanged.</summary>
        /// <exception cref="ArgumentException"><paramref name="operation"/> is <see langword="null"/>, empty or white space.</exception>
        public Result<T> Record(string operation)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(operation);

            if (result.TryGetError(out var error))
            {
                ResultTelemetry.Record(error, operation);
            }

            return result;
        }
    }

    /// <param name="source">The pending result to report.</param>
    extension(Task<Result> source)
    {
        /// <summary>Awaits the result, reports a failure of <paramref name="operation"/>, and returns the result unchanged.</summary>
        /// <remarks>When the task has already completed, it is returned itself.</remarks>
        /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="operation"/> is <see langword="null"/>, empty or white space.</exception>
        public Task<Result> RecordAsync(string operation)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentException.ThrowIfNullOrWhiteSpace(operation);

            if (source.IsCompletedSuccessfully)
            {
                _ = source.Result.Record(operation);
                return source;
            }

            return Core(source, operation);

            static async Task<Result> Core(Task<Result> source, string operation) =>
                (await source.ConfigureAwait(false)).Record(operation);
        }
    }

    /// <typeparam name="T">The type of the success value.</typeparam>
    /// <param name="source">The pending result to report.</param>
    extension<T>(Task<Result<T>> source)
        where T : notnull
    {
        /// <summary>Awaits the result, reports a failure of <paramref name="operation"/>, and returns the result unchanged.</summary>
        /// <remarks>When the task has already completed, it is returned itself.</remarks>
        /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="operation"/> is <see langword="null"/>, empty or white space.</exception>
        public Task<Result<T>> RecordAsync(string operation)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentException.ThrowIfNullOrWhiteSpace(operation);

            if (source.IsCompletedSuccessfully)
            {
                _ = source.Result.Record(operation);
                return source;
            }

            return Core(source, operation);

            static async Task<Result<T>> Core(Task<Result<T>> source, string operation) =>
                (await source.ConfigureAwait(false)).Record(operation);
        }
    }
}
