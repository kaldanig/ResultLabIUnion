namespace ResultUnion;

// CombineAll for results that carry values: every value on success, every error on failure.
public readonly partial record struct Result
{
    /// <summary>Combines two results: a success with both values, or a failure that reports every error.</summary>
    /// <remarks>
    /// <para>
    /// Typical use is creating several value objects for one command:
    /// <c>Result.CombineAll(Email.Create(cmd.Email), Password.Create(cmd.Password)).Bind(User.Create)</c>.
    /// The tuple overloads of <c>Map</c> and <c>Bind</c> in <see cref="ResultTupleExtensions"/> take one parameter per value.
    /// </para>
    /// <para>
    /// As with <see cref="CombineAll(ReadOnlySpan{Result})"/>, a single failure is returned unchanged and several are grouped,
    /// in argument order, into <see cref="Error.Validation(IEnumerable{Error})"/>.
    /// </para>
    /// </remarks>
    public static Result<(T1, T2)> CombineAll<T1, T2>(Result<T1> first, Result<T2> second)
        where T1 : notnull
        where T2 : notnull
    {
        var outcome = CombineAll(first.ToResult(), second.ToResult());

        return outcome.TryGetError(out var error)
            ? Result<(T1, T2)>.Failure(error)
            : Result<(T1, T2)>.Success((first.ResultValue, second.ResultValue));
    }

    /// <summary>Combines three results: a success with every value, or a failure that reports every error.</summary>
    /// <remarks>A single failure is returned unchanged; several are grouped, in argument order, into <see cref="Error.Validation(IEnumerable{Error})"/>.</remarks>
    public static Result<(T1, T2, T3)> CombineAll<T1, T2, T3>(Result<T1> first, Result<T2> second, Result<T3> third)
        where T1 : notnull
        where T2 : notnull
        where T3 : notnull
    {
        var outcome = CombineAll(first.ToResult(), second.ToResult(), third.ToResult());

        return outcome.TryGetError(out var error)
            ? Result<(T1, T2, T3)>.Failure(error)
            : Result<(T1, T2, T3)>.Success((first.ResultValue, second.ResultValue, third.ResultValue));
    }

    /// <summary>Combines four results: a success with every value, or a failure that reports every error.</summary>
    /// <remarks>A single failure is returned unchanged; several are grouped, in argument order, into <see cref="Error.Validation(IEnumerable{Error})"/>.</remarks>
    public static Result<(T1, T2, T3, T4)> CombineAll<T1, T2, T3, T4>(Result<T1> first, Result<T2> second, Result<T3> third, Result<T4> fourth)
        where T1 : notnull
        where T2 : notnull
        where T3 : notnull
        where T4 : notnull
    {
        var outcome = CombineAll(first.ToResult(), second.ToResult(), third.ToResult(), fourth.ToResult());

        return outcome.TryGetError(out var error)
            ? Result<(T1, T2, T3, T4)>.Failure(error)
            : Result<(T1, T2, T3, T4)>.Success((first.ResultValue, second.ResultValue, third.ResultValue, fourth.ResultValue));
    }
}
