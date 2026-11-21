namespace ResultUnion;

/// <summary>
/// <c>Map</c> and <c>Bind</c> for results of tuples, taking one delegate parameter per element, so the values produced by
/// <see cref="Result.CombineAll{T1, T2}(Result{T1}, Result{T2})"/> can go straight into a factory:
/// <c>Result.CombineAll(email, password).Bind(User.Create)</c>.
/// </summary>
/// <remarks>Failures are propagated unchanged and the delegate is not invoked. A <see langword="null"/> delegate throws <see cref="ArgumentNullException"/>.</remarks>
public static class ResultTupleExtensions
{
    /// <typeparam name="T1">The type of the first value.</typeparam>
    /// <typeparam name="T2">The type of the second value.</typeparam>
    /// <param name="result">A result carrying two values.</param>
    extension<T1, T2>(Result<(T1, T2)> result)
        where T1 : notnull
        where T2 : notnull
    {
        /// <summary>Transforms both values of a successful result; failures are propagated unchanged.</summary>
        public Result<TResult> Map<TResult>(Func<T1, T2, TResult> mapper)
            where TResult : notnull
        {
            ArgumentNullException.ThrowIfNull(mapper);

            return result.TryGetResultValue(out var values)
                ? Result<TResult>.Success(mapper(values.Item1, values.Item2))
                : Result<TResult>.Failure(result.Error);
        }

        /// <summary>Chains an operation that may fail and takes both values, when this result is a success; failures are propagated unchanged.</summary>
        public Result<TResult> Bind<TResult>(Func<T1, T2, Result<TResult>> next)
            where TResult : notnull
        {
            ArgumentNullException.ThrowIfNull(next);

            return result.TryGetResultValue(out var values)
                ? next(values.Item1, values.Item2)
                : Result<TResult>.Failure(result.Error);
        }
    }

    /// <typeparam name="T1">The type of the first value.</typeparam>
    /// <typeparam name="T2">The type of the second value.</typeparam>
    /// <typeparam name="T3">The type of the third value.</typeparam>
    /// <param name="result">A result carrying three values.</param>
    extension<T1, T2, T3>(Result<(T1, T2, T3)> result)
        where T1 : notnull
        where T2 : notnull
        where T3 : notnull
    {
        /// <summary>Transforms the three values of a successful result; failures are propagated unchanged.</summary>
        public Result<TResult> Map<TResult>(Func<T1, T2, T3, TResult> mapper)
            where TResult : notnull
        {
            ArgumentNullException.ThrowIfNull(mapper);

            return result.TryGetResultValue(out var values)
                ? Result<TResult>.Success(mapper(values.Item1, values.Item2, values.Item3))
                : Result<TResult>.Failure(result.Error);
        }

        /// <summary>Chains an operation that may fail and takes the three values, when this result is a success; failures are propagated unchanged.</summary>
        public Result<TResult> Bind<TResult>(Func<T1, T2, T3, Result<TResult>> next)
            where TResult : notnull
        {
            ArgumentNullException.ThrowIfNull(next);

            return result.TryGetResultValue(out var values)
                ? next(values.Item1, values.Item2, values.Item3)
                : Result<TResult>.Failure(result.Error);
        }
    }

    /// <typeparam name="T1">The type of the first value.</typeparam>
    /// <typeparam name="T2">The type of the second value.</typeparam>
    /// <typeparam name="T3">The type of the third value.</typeparam>
    /// <typeparam name="T4">The type of the fourth value.</typeparam>
    /// <param name="result">A result carrying four values.</param>
    extension<T1, T2, T3, T4>(Result<(T1, T2, T3, T4)> result)
        where T1 : notnull
        where T2 : notnull
        where T3 : notnull
        where T4 : notnull
    {
        /// <summary>Transforms the four values of a successful result; failures are propagated unchanged.</summary>
        public Result<TResult> Map<TResult>(Func<T1, T2, T3, T4, TResult> mapper)
            where TResult : notnull
        {
            ArgumentNullException.ThrowIfNull(mapper);

            return result.TryGetResultValue(out var values)
                ? Result<TResult>.Success(mapper(values.Item1, values.Item2, values.Item3, values.Item4))
                : Result<TResult>.Failure(result.Error);
        }

        /// <summary>Chains an operation that may fail and takes the four values, when this result is a success; failures are propagated unchanged.</summary>
        public Result<TResult> Bind<TResult>(Func<T1, T2, T3, T4, Result<TResult>> next)
            where TResult : notnull
        {
            ArgumentNullException.ThrowIfNull(next);

            return result.TryGetResultValue(out var values)
                ? next(values.Item1, values.Item2, values.Item3, values.Item4)
                : Result<TResult>.Failure(result.Error);
        }
    }
}
