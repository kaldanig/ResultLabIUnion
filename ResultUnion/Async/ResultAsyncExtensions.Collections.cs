namespace ResultUnion;

public static partial class ResultAsyncExtensions
{
    /// <typeparam name="TSource">The type of the input elements.</typeparam>
    /// <param name="source">The inputs to turn into results.</param>
    extension<TSource>(IEnumerable<TSource> source)
    {
        /// <summary>
        /// Turns every element into a result with an asynchronous <paramref name="selector"/>, then combines them: a success with every
        /// value, in order, or a failure that reports every error.
        /// </summary>
        /// <param name="selector">Creates the result for one element, e.g. a lookup.</param>
        /// <param name="field">When set, the name of the collection, used to prefix each error's field with <c>field[index]</c>.</param>
        /// <remarks>
        /// Elements are processed <b>one after another</b>, never concurrently, so the selector may use a resource that does not
        /// allow parallel calls, such as an EF Core <c>DbContext</c>. Every element is processed, even after a failure.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="source"/> or <paramref name="selector"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="field"/> is empty or white space.</exception>
        public Task<Result<IReadOnlyList<T>>> CombineAllAsync<T>(Func<TSource, Task<Result<T>>> selector, string? field = null)
            where T : notnull
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(selector);
            ResultCollectionExtensions.ThrowIfBlank(field);

            return Core(source, selector, field);

            static async Task<Result<IReadOnlyList<T>>> Core(IEnumerable<TSource> source, Func<TSource, Task<Result<T>>> selector, string? field)
            {
                var collector = new ResultCollectionExtensions.Collector<T>(field);

                foreach (var element in source)
                {
                    collector.Add(await selector(element).ConfigureAwait(false));
                }

                return collector.ToResult();
            }
        }
    }
}
