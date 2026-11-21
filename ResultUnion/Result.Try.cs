namespace ResultUnion;

// Try for one specific exception type: everything else propagates.
public readonly partial record struct Result
{
    /// <summary>Runs code that may throw, turning only exceptions of type <typeparamref name="TException"/> into a failure; any other exception propagates.</summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <typeparam name="TException">The exception type to translate. It is inferred from a typed lambda parameter.</typeparam>
    /// <remarks>
    /// <para>
    /// Use it when one exception means something to the caller and the rest are bugs or outages that should surface:
    /// <c>Result.Try(() => int.Parse(text), (FormatException ex) => Error.Validation(ex.Message))</c>.
    /// </para>
    /// <para>
    /// <see cref="OperationCanceledException"/> is never caught, even when <typeparamref name="TException"/> is a base type of it:
    /// cancellation is not a business failure. As with <see cref="Try{T}(Func{T}, Func{Exception, Error})"/>, only
    /// <paramref name="action"/> is guarded, and an <see langword="async"/> lambda must go to <c>TryAsync</c> instead.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">A delegate is <see langword="null"/>, <paramref name="action"/> returns <see langword="null"/>, or <paramref name="onException"/> returns <see langword="null"/>.</exception>
    public static Result<T> Try<T, TException>(Func<T> action, Func<TException, Error> onException)
        where T : notnull
        where TException : Exception
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(onException);

        T value;

        try
        {
            value = action();
        }
        catch (TException exception) when (exception is not OperationCanceledException)
        {
            return Result<T>.Failure(onException(exception));
        }

        return Result<T>.Success(value);
    }

    /// <summary>Runs code that may throw and produces no value, turning only exceptions of type <typeparamref name="TException"/> into a failure; any other exception propagates.</summary>
    /// <typeparam name="TException">The exception type to translate. It is inferred from a typed lambda parameter.</typeparam>
    /// <remarks>
    /// For example <c>Result.Try(() => File.Delete(path), (IOException ex) => Error.Unavailable(ex.Message))</c>.
    /// <see cref="OperationCanceledException"/> is never caught.
    /// </remarks>
    /// <exception cref="ArgumentNullException">A delegate is <see langword="null"/>, or <paramref name="onException"/> returns <see langword="null"/>.</exception>
    public static Result Try<TException>(Action action, Func<TException, Error> onException)
        where TException : Exception
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(onException);

        try
        {
            action();
        }
        catch (TException exception) when (exception is not OperationCanceledException)
        {
            return Failure(onException(exception));
        }

        return Success();
    }
}
