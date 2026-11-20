namespace ResultUnion;

public static partial class ResultAsyncExtensions
{
    /// <typeparam name="T">The type of the looked-up reference.</typeparam>
    /// <param name="source">A pending lookup that reports "not found" as <see langword="null"/>.</param>
    extension<T>(Task<T?> source)
        where T : class
    {
        /// <summary>Awaits a lookup and turns it into a result: a success with the value, or a failure carrying <paramref name="errorIfNull"/> when it is <see langword="null"/>.</summary>
        /// <remarks>
        /// For APIs such as EF Core's <c>FirstOrDefaultAsync</c>:
        /// <c>await users.FirstOrDefaultAsync(u => u.Id == id).ToResultAsync(Error.NotFound("…"))</c>.
        /// An exception from the lookup itself is not caught; it surfaces through the returned task.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="source"/> or <paramref name="errorIfNull"/> is <see langword="null"/>.</exception>
        public Task<Result<T>> ToResultAsync(Error errorIfNull)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(errorIfNull);

            return Core(source, errorIfNull);

            static async Task<Result<T>> Core(Task<T?> source, Error errorIfNull) =>
                Result.FromNullable(await source.ConfigureAwait(false), errorIfNull);
        }
    }

    /// <typeparam name="T">The type of the looked-up value.</typeparam>
    /// <param name="source">A pending lookup that reports "not found" as <see langword="null"/>.</param>
    extension<T>(Task<T?> source)
        where T : struct
    {
        /// <summary>Awaits a lookup and turns it into a result: a success with the value, or a failure carrying <paramref name="errorIfNull"/> when it has no value.</summary>
        /// <remarks>An exception from the lookup itself is not caught; it surfaces through the returned task.</remarks>
        /// <exception cref="ArgumentNullException"><paramref name="source"/> or <paramref name="errorIfNull"/> is <see langword="null"/>.</exception>
        public Task<Result<T>> ToResultAsync(Error errorIfNull)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(errorIfNull);

            return Core(source, errorIfNull);

            static async Task<Result<T>> Core(Task<T?> source, Error errorIfNull) =>
                Result.FromNullable(await source.ConfigureAwait(false), errorIfNull);
        }
    }
}
