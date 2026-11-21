using System.Diagnostics.CodeAnalysis;
namespace ResultUnion;

/// <summary>
/// <c>CombineAll</c> for sequences: every value of a collection, or every error with the index of the element it came from.
/// </summary>
/// <remarks>
/// <para>
/// Typical use is a command with a list, such as order lines:
/// <c>command.Lines.CombineAll(OrderLine.Create, field: "lines")</c> returns <c>Result&lt;IReadOnlyList&lt;OrderLine&gt;&gt;</c>.
/// </para>
/// <para>
/// As with the other <c>CombineAll</c> overloads, a single failure is returned unchanged and several are grouped, in order,
/// into <see cref="Error.Validation(IEnumerable{Error})"/>. With a <c>field</c>, every error gets the path
/// <c>field[index]</c> in front of its own field (<see cref="Error.WithFieldPrefix(string)"/>): <c>lines[2].quantity</c>.
/// </para>
/// <para>The sequence is enumerated once. An empty sequence is a success with an empty list.</para>
/// </remarks>
public static class ResultCollectionExtensions
{
    /// <typeparam name="T">The type of the values.</typeparam>
    /// <param name="results">The results to combine.</param>
    extension<T>(IEnumerable<Result<T>> results)
        where T : notnull
    {
        /// <summary>Combines a sequence of results: a success with every value, in order, or a failure that reports every error.</summary>
        /// <param name="field">When set, the name of the collection, used to prefix each error's field with <c>field[index]</c>.</param>
        /// <exception cref="ArgumentNullException"><paramref name="results"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="field"/> is empty or white space.</exception>
        [SuppressMessage("ApiDesign", "RS0026", Justification = "Different receivers in one static class, not real overloads. Add new options as new overloads, never as new optional parameters.")]
        public Result<IReadOnlyList<T>> CombineAll(string? field = null)
        {
            ArgumentNullException.ThrowIfNull(results);
            ThrowIfBlank(field);

            var collector = new Collector<T>(field);

            foreach (var result in results)
            {
                collector.Add(result);
            }

            return collector.ToResult();
        }
    }

    /// <typeparam name="TSource">The type of the input elements.</typeparam>
    /// <param name="source">The inputs to turn into results.</param>
    extension<TSource>(IEnumerable<TSource> source)
    {
        /// <summary>Turns every element into a result with <paramref name="selector"/>, then combines them like <see cref="CombineAll{T}(IEnumerable{Result{T}}, string)"/>.</summary>
        /// <param name="selector">Creates the result for one element, e.g. a value object factory.</param>
        /// <param name="field">When set, the name of the collection, used to prefix each error's field with <c>field[index]</c>.</param>
        /// <remarks>Every element is processed, even after a failure, so that every error is reported.</remarks>
        /// <exception cref="ArgumentNullException"><paramref name="source"/> or <paramref name="selector"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="field"/> is empty or white space.</exception>
        [SuppressMessage("ApiDesign", "RS0026", Justification = "Different receivers in one static class, not real overloads. Add new options as new overloads, never as new optional parameters.")]
        public Result<IReadOnlyList<T>> CombineAll<T>(Func<TSource, Result<T>> selector, string? field = null)
            where T : notnull
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(selector);
            ThrowIfBlank(field);

            var collector = new Collector<T>(field);

            foreach (var element in source)
            {
                collector.Add(selector(element));
            }

            return collector.ToResult();
        }
    }

    internal static void ThrowIfBlank(string? field)
    {
        if (field is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(field);
        }
    }

    /// <summary>Accumulates values and errors in order, tracking the index of each element.</summary>
    internal sealed class Collector<T>(string? field)
        where T : notnull
    {
        private readonly List<T> _values = [];
        private List<Error>? _errors;
        private int _index;

        public void Add(Result<T> result)
        {
            if (result.TryGetResultValue(out var value))
            {
                _values.Add(value);
            }
            else
            {
                (_errors ??= []).Add(field is null ? result.Error : result.Error.WithFieldPrefix($"{field}[{_index}]"));
            }

            _index++;
        }

        public Result<IReadOnlyList<T>> ToResult() => _errors switch
        {
            null => Result<IReadOnlyList<T>>.Success(_values.AsReadOnly()),
            [var single] => Result<IReadOnlyList<T>>.Failure(single),
            _ => Result<IReadOnlyList<T>>.Failure(Error.Validation(_errors)),
        };
    }
}
