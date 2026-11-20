using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace ResultUnion;

/// <summary>The outcome of an operation that either succeeds or fails with an <see cref="ResultUnion.Error"/>.</summary>
/// <remarks>Lets results of different value types be handled together, for example by <see cref="Result.Combine(IEnumerable{IResultState})"/>.</remarks>
public interface IResultState
{
    /// <summary>Gets a value indicating whether the operation succeeded.</summary>
    bool IsSuccess { get; }

    /// <summary>Gets a value indicating whether the operation failed.</summary>
    bool IsFailure { get; }

    /// <summary>Gets the error that caused the failure.</summary>
    /// <remarks>A <see langword="default"/>-constructed result is a failure carrying <see cref="Error.DefaultResult"/>.</remarks>
    /// <exception cref="InvalidOperationException">The result is a success.</exception>
    Error Error { get; }
}

/// <summary>The outcome of an operation that either succeeds with a <typeparamref name="T"/> value or fails with an <see cref="ResultUnion.Error"/>.</summary>
/// <typeparam name="T">The type of the success value.</typeparam>
public interface IResultState<out T> : IResultState
    where T : notnull
{
    /// <summary>Gets the value produced by the operation.</summary>
    /// <exception cref="InvalidOperationException">The result is a failure.</exception>
    T ResultValue { get; }
}

/// <summary>
/// The outcome of an operation that produces a <typeparamref name="T"/> value: a union of two cases,
/// the value on success or an <see cref="ResultUnion.Error"/> on failure.
/// </summary>
/// <typeparam name="T">The type of the success value. It cannot be <see langword="null"/>.</typeparam>
/// <remarks>
/// <para>
/// Being a union, a result can be pattern matched on its cases:
/// <c>result switch { T value => ..., Error error => ... }</c>. The compiler checks that both cases are handled.
/// </para>
/// <para>
/// Each case converts implicitly to the union, so a method returning <see cref="Result{T}"/> can
/// <c>return value;</c> or <c>return error;</c>. This union conversion comes from the C# 15 compiler, not from
/// user-defined operators, so it is not available to VB, F# or projects that set <c>LangVersion</c> below 15:
/// they use <see cref="Success(T)"/> and <see cref="Failure(ResultUnion.Error)"/> instead.
/// </para>
/// <para>
/// A <see langword="default"/>-constructed result is a failure carrying <see cref="Error.DefaultResult"/>.
/// </para>
/// <para>
/// Every method that takes a delegate throws <see cref="ArgumentNullException"/> when that delegate is
/// <see langword="null"/>, even when the result's state means the delegate would not be invoked.
/// </para>
/// </remarks>
[Union]
public readonly record struct Result<T> : IUnion, IResultState<T>
    where T : notnull
{
    private readonly T? _value;

    /// <inheritdoc/>
    public bool IsSuccess { get; }

    /// <inheritdoc/>
    public bool IsFailure => !IsSuccess;

    /// <summary>Gets the current case of the union: the <typeparamref name="T"/> value on success, or the <see cref="ResultUnion.Error"/> on failure.</summary>
    /// <remarks>
    /// The case is decided by <see cref="IsSuccess"/>, not by checking the stored value for <see langword="null"/>:
    /// for a value-type <typeparamref name="T"/> the stored value is never <see langword="null"/>, so it cannot signal "no value".
    /// Reading this property boxes a value-type <typeparamref name="T"/>. Pattern matching uses the non-boxing
    /// <see cref="TryGetValue(out T)"/> overloads instead.
    /// </remarks>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public object? Value => IsSuccess ? _value : Error;

    /// <summary>Gets a value indicating whether the union holds a case. Always <see langword="true"/>.</summary>
    /// <remarks>Even a <see langword="default"/>-constructed result holds a case: a failure carrying <see cref="Error.DefaultResult"/>.</remarks>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool HasValue => true;

    /// <summary>Attempts to read the success value without boxing.</summary>
    /// <param name="value">The value, when this method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when the result is a success; otherwise <see langword="false"/>.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool TryGetValue([MaybeNullWhen(false)] out T value)
    {
        value = IsSuccess ? _value! : default;

        return IsSuccess;
    }

    /// <summary>Attempts to read the error without throwing.</summary>
    /// <param name="error">The error, when this method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when the result is a failure; otherwise <see langword="false"/>.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool TryGetValue([NotNullWhen(true)] out Error? error)
    {
        error = IsFailure ? Error : null;

        return IsFailure;
    }

    /// <inheritdoc/>
    /// <remarks>Named <c>ResultValue</c> because <see cref="Value"/> is the union's case, as required by <see cref="IUnion"/>.</remarks>
    public T ResultValue => IsSuccess
        ? _value!
        : throw new InvalidOperationException("Cannot access 'ResultValue' when the 'Result' is a failure.");

    /// <inheritdoc/>
    public Error Error => IsFailure
        ? field ?? Error.DefaultResult
        : throw new InvalidOperationException("Cannot access 'Error' when the 'Result' is a success.");

    /// <summary>Creates a successful result carrying <paramref name="value"/>.</summary>
    /// <param name="value">The success value.</param>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public Result(T value)
    {
        // 'T : notnull' is enforced at compile time (CS8714), so this only guards callers
        // that pass 'null!' or come from a nullable-oblivious assembly.
        if (!typeof(T).IsValueType && value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        IsSuccess = true;

        _value = value;
        Error = null;
    }

    /// <summary>Creates a failed result carrying <paramref name="error"/>.</summary>
    /// <param name="error">The error that caused the failure.</param>
    /// <exception cref="ArgumentNullException"><paramref name="error"/> is <see langword="null"/>.</exception>
    public Result(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);

        IsSuccess = false;

        Error = error;
        _value = default;
    }

    /// <summary>Creates a successful result carrying <paramref name="value"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public static Result<T> Success(T value) => new(value);

    /// <summary>Creates a failed result carrying <paramref name="error"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="error"/> is <see langword="null"/>.</exception>
    public static Result<T> Failure(Error error) => new(error);

    /// <summary>Projects the result into a single value by invoking the branch that matches its state.</summary>
    public TResult Match<TResult>(Func<T, TResult> onSuccess, Func<Error, TResult> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);

        return IsSuccess
            ? onSuccess(_value!)
            : onFailure(Error);
    }

    /// <summary>Invokes the branch that matches the state of the result.</summary>
    public void Match(Action<T> onSuccess, Action<Error> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);

        if (IsSuccess)
        {
            onSuccess(_value!);
        }
        else
        {
            onFailure(Error);
        }
    }

    /// <summary>Returns the value on success, or <see langword="default"/> on failure.</summary>
    public T? GetValueOrDefault() => IsSuccess ? _value : default;

    /// <summary>Returns the value on success, or <paramref name="defaultValue"/> on failure.</summary>
    public T GetValueOrDefault(T defaultValue) => IsSuccess ? _value! : defaultValue;

    /// <summary>Discards the value, keeping only success or failure.</summary>
    public Result ToResult() => IsSuccess
        ? Result.Success()
        : Result.Failure(Error);

    /// <summary>Attempts to read the success value without throwing or boxing.</summary>
    /// <param name="value">The value, when this method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when the result is a success; otherwise <see langword="false"/>.</returns>
    /// <remarks>
    /// The visible counterpart of the union's <see cref="TryGetValue(out T)"/>. That one is hidden because its
    /// <see cref="TryGetValue(out ResultUnion.Error)"/> overload makes <c>out var</c> ambiguous.
    /// </remarks>
    public bool TryGetResultValue([MaybeNullWhen(false)] out T value) => TryGetValue(out value);

    /// <summary>Attempts to read the error without throwing.</summary>
    /// <param name="error">The error, when this method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when the result is a failure; otherwise <see langword="false"/>.</returns>
    public bool TryGetError([NotNullWhen(true)] out Error? error) => TryGetValue(out error);

    /// <summary>Transforms the value of a successful result; failures are propagated unchanged.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="mapper"/> is <see langword="null"/>, or returns <see langword="null"/>.</exception>
    public Result<TResult> Map<TResult>(Func<T, TResult> mapper)
        where TResult : notnull
    {
        ArgumentNullException.ThrowIfNull(mapper);

        return IsSuccess
            ? Result<TResult>.Success(mapper(_value!))
            : Result<TResult>.Failure(Error);
    }

    /// <summary>Chains a value-producing operation that may fail, when this result is a success; failures are propagated unchanged.</summary>
    public Result<TResult> Bind<TResult>(Func<T, Result<TResult>> next)
        where TResult : notnull
    {
        ArgumentNullException.ThrowIfNull(next);

        return IsSuccess
            ? next(_value!)
            : Result<TResult>.Failure(Error);
    }

    /// <summary>Chains an operation that may fail and produces no value, when this result is a success; failures are propagated unchanged.</summary>
    public Result Bind(Func<T, Result> next)
    {
        ArgumentNullException.ThrowIfNull(next);

        return IsSuccess
            ? next(_value!)
            : Result.Failure(Error);
    }

    /// <summary>Transforms the error of a failed result; successes are propagated unchanged.</summary>
    public Result<T> MapError(Func<Error, Error> mapper)
    {
        ArgumentNullException.ThrowIfNull(mapper);

        return IsSuccess
            ? this
            : Failure(mapper(Error));
    }

    /// <summary>Turns a successful result into a failure carrying <paramref name="error"/> when <paramref name="predicate"/> is not satisfied.</summary>
    /// <remarks>Failures are propagated unchanged and <paramref name="predicate"/> is not invoked.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="predicate"/> or <paramref name="error"/> is <see langword="null"/>.</exception>
    public Result<T> Ensure(Func<T, bool> predicate, Error error)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(error);

        return IsFailure || predicate(_value!)
            ? this
            : Failure(error);
    }

    /// <summary>Turns a successful result into a failure built from the value by <paramref name="errorFactory"/> when <paramref name="predicate"/> is not satisfied.</summary>
    /// <remarks>Failures are propagated unchanged and neither delegate is invoked; <paramref name="errorFactory"/> is invoked only when <paramref name="predicate"/> fails.</remarks>
    /// <exception cref="ArgumentNullException">A delegate is <see langword="null"/>, or <paramref name="errorFactory"/> returns <see langword="null"/>.</exception>
    public Result<T> Ensure(Func<T, bool> predicate, Func<T, Error> errorFactory)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(errorFactory);

        return IsFailure || predicate(_value!)
            ? this
            : Failure(errorFactory(_value!));
    }

    /// <summary>Runs a validation that returns a <see cref="Result"/>; on success keeps the value, on failure returns its error.</summary>
    /// <remarks>
    /// Lets an existing <c>Result</c>-returning validator be reused without losing the value, e.g. <c>.Ensure(EnsureEven)</c>
    /// instead of <c>.Bind(n => EnsureEven(n).Bind(() => Result.Success(n)))</c>.
    /// Failures are propagated unchanged and <paramref name="check"/> is not invoked.
    /// </remarks>
    public Result<T> Ensure(Func<T, Result> check)
    {
        ArgumentNullException.ThrowIfNull(check);

        if (IsFailure)
        {
            return this;
        }

        var outcome = check(_value!);

        return outcome.IsSuccess
            ? this
            : Failure(outcome.Error);
    }

    /// <summary>Invokes a side effect with the value when the result is a success, then returns the result unchanged.</summary>
    public Result<T> Tap(Action<T> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (IsSuccess)
        {
            action(_value!);
        }

        return this;
    }

    /// <summary>Invokes a side effect with the error when the result is a failure, then returns the result unchanged.</summary>
    public Result<T> TapError(Action<Error> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (IsFailure)
        {
            action(Error);
        }

        return this;
    }

    /// <inheritdoc/>
    public override string ToString() =>
        IsSuccess
            ? $"Success({_value})"
            : $"Failure({Error})";
}

/// <summary>The outcome of an operation that produces no value: it either succeeds or fails with an <see cref="ResultUnion.Error"/>.</summary>
/// <remarks>
/// <para>Also hosts the generic factories <see cref="Success{T}(T)"/> and <see cref="Failure{T}(ResultUnion.Error)"/>, so the value type can be inferred.</para>
/// <para>A <see langword="default"/>-constructed result is a failure carrying <see cref="Error.DefaultResult"/>.</para>
/// <para>
/// Every method that takes a delegate throws <see cref="ArgumentNullException"/> when that delegate is
/// <see langword="null"/>, even when the result's state means the delegate would not be invoked.
/// </para>
/// </remarks>
public readonly record struct Result : IResultState
{
    /// <inheritdoc/>
    public bool IsSuccess { get; }

    /// <inheritdoc/>
    public bool IsFailure => !IsSuccess;

    /// <inheritdoc/>
    public Error Error => IsFailure
        ? field ?? Error.DefaultResult
        : throw new InvalidOperationException("Cannot access 'Error' when the 'Result' is a success.");

    private Result(bool isSuccess)
    {
        IsSuccess = isSuccess;

        Error = null;
    }

    private Result(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);

        IsSuccess = false;

        Error = error;
    }

    /// <summary>Creates a successful result.</summary>
    public static Result Success() => new(true);

    /// <summary>Creates a failed result carrying <paramref name="error"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="error"/> is <see langword="null"/>.</exception>
    public static Result Failure(Error error) => new(error);

    /// <summary>Creates a successful result carrying <paramref name="value"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public static Result<T> Success<T>(T value)
        where T : notnull
        => Result<T>.Success(value);

    /// <summary>Creates a failed result of <typeparamref name="T"/> carrying <paramref name="error"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="error"/> is <see langword="null"/>.</exception>
    public static Result<T> Failure<T>(Error error)
        where T : notnull
        => Result<T>.Failure(error);

    /// <summary>Runs code that may throw, turning an exception into a failure built by <paramref name="onException"/>.</summary>
    /// <remarks>
    /// <para>
    /// Use it at the boundary with code that reports failures by throwing, such as parsers, file or network APIs.
    /// <see cref="OperationCanceledException"/> is never caught: cancellation is not a business failure.
    /// </para>
    /// <para>
    /// Only <paramref name="action"/> is guarded. An exception thrown by <paramref name="onException"/>, or the
    /// <see cref="ArgumentNullException"/> raised when <paramref name="action"/> returns <see langword="null"/>, propagates.
    /// </para>
    /// <para>
    /// Do not pass asynchronous code: an <see langword="async"/> lambda would make <typeparamref name="T"/> a <see cref="Task"/>,
    /// and exceptions thrown after its first <see langword="await"/> would not be observed. Use
    /// <see cref="ResultAsyncExtensions.TryAsync{T}(Func{Task{T}}, Func{Exception, Error})"/> instead.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">A delegate is <see langword="null"/>, <paramref name="action"/> returns <see langword="null"/>, or <paramref name="onException"/> returns <see langword="null"/>.</exception>
    public static Result<T> Try<T>(Func<T> action, Func<Exception, Error> onException)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(onException);

        T value;

        try
        {
            value = action();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Result<T>.Failure(onException(exception));
        }

        return Result<T>.Success(value);
    }

    /// <summary>Runs code that may throw and produces no value, turning an exception into a failure built by <paramref name="onException"/>.</summary>
    /// <remarks>
    /// <see cref="OperationCanceledException"/> is never caught, and an exception thrown by <paramref name="onException"/> propagates.
    /// Do not pass an <see langword="async"/> lambda: it would run as <c>async void</c> and its exceptions would not be observed.
    /// Use <see cref="ResultAsyncExtensions.TryAsync(Func{Task}, Func{Exception, Error})"/> instead.
    /// </remarks>
    /// <exception cref="ArgumentNullException">A delegate is <see langword="null"/>, or <paramref name="onException"/> returns <see langword="null"/>.</exception>
    public static Result Try(Action action, Func<Exception, Error> onException)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(onException);

        try
        {
            action();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Failure(onException(exception));
        }

        return Success();
    }

    /// <summary>Turns a possibly <see langword="null"/> reference into a result: a success with the value, or a failure carrying <paramref name="errorIfNull"/>.</summary>
    /// <remarks>
    /// Use it with APIs that report "not found" as <see langword="null"/>, e.g.
    /// <c>Result.FromNullable(cache.GetValueOrDefault(key), Error.NotFound("…"))</c>.
    /// For a pending lookup use <see cref="ResultAsyncExtensions.ToResultAsync{T}(Task{T}, Error)"/>.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="errorIfNull"/> is <see langword="null"/>.</exception>
    public static Result<T> FromNullable<T>(T? value, Error errorIfNull)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(errorIfNull);

        return value is not null
            ? Result<T>.Success(value)
            : Result<T>.Failure(errorIfNull);
    }

    /// <summary>Turns a nullable value into a result: a success with the value, or a failure carrying <paramref name="errorIfNull"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="errorIfNull"/> is <see langword="null"/>.</exception>
    public static Result<T> FromNullable<T>(T? value, Error errorIfNull)
        where T : struct
    {
        ArgumentNullException.ThrowIfNull(errorIfNull);

        return value.HasValue
            ? Result<T>.Success(value.Value)
            : Result<T>.Failure(errorIfNull);
    }

    /// <summary>Returns a success when every result succeeded; otherwise the first failure, in order.</summary>
    public static Result Combine(params ReadOnlySpan<Result> results)
    {
        foreach (var result in results)
        {
            if (result.IsFailure)
            {
                return result;
            }
        }

        return Success();
    }

    /// <summary>Returns a success when every result succeeded; otherwise the first failure, in enumeration order.</summary>
    /// <remarks>Accepts <see cref="Result{T}"/> instances too, at the cost of boxing each one. Enumeration stops at the first failure.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="results"/> is <see langword="null"/>, or contains a <see langword="null"/> element before the first failure.</exception>
    public static Result Combine(IEnumerable<IResultState> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        foreach (var result in results)
        {
            ArgumentNullException.ThrowIfNull(result, nameof(results));

            if (result.IsFailure)
            {
                return Failure(result.Error);
            }
        }

        return Success();
    }

    /// <summary>Returns a success when every result succeeded; otherwise a failure that reports every error, not just the first.</summary>
    /// <remarks>
    /// A single failure is returned unchanged. Several failures are grouped, in order, into
    /// <see cref="Error.Validation(IEnumerable{Error})"/>: its <see cref="Error.Details"/> hold the individual errors.
    /// Name each input with <see cref="Error.WithField(string)"/> first, so the details can be reported per field.
    /// </remarks>
    public static Result CombineAll(params ReadOnlySpan<Result> results)
    {
        List<Error>? errors = null;

        foreach (var result in results)
        {
            if (result.TryGetError(out var error))
            {
                (errors ??= []).Add(error);
            }
        }

        return FromErrors(errors);
    }

    /// <summary>Returns a success when every result succeeded; otherwise a failure that reports every error, in enumeration order.</summary>
    /// <remarks>
    /// Accepts <see cref="Result{T}"/> instances too, at the cost of boxing each one. A single failure is returned unchanged;
    /// several are grouped into <see cref="Error.Validation(IEnumerable{Error})"/>.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="results"/> is <see langword="null"/>, or contains a <see langword="null"/> element.</exception>
    public static Result CombineAll(IEnumerable<IResultState> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        List<Error>? errors = null;

        foreach (var result in results)
        {
            ArgumentNullException.ThrowIfNull(result, nameof(results));

            if (result.IsFailure)
            {
                (errors ??= []).Add(result.Error);
            }
        }

        return FromErrors(errors);
    }

    private static Result FromErrors(List<Error>? errors) => errors switch
    {
        null => Success(),
        [var single] => Failure(single),
        _ => Failure(Error.Validation(errors)),
    };

    /// <summary>Attempts to read the error without throwing.</summary>
    /// <param name="error">The error, when this method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when the result is a failure; otherwise <see langword="false"/>.</returns>
    public bool TryGetError([NotNullWhen(true)] out Error? error)
    {
        error = IsFailure ? Error : null;

        return IsFailure;
    }

    /// <summary>Projects the result into a single value by invoking the branch that matches its state.</summary>
    public TResult Match<TResult>(Func<TResult> onSuccess, Func<Error, TResult> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);

        return IsSuccess
            ? onSuccess()
            : onFailure(Error);
    }

    /// <summary>Invokes the branch that matches the state of the result.</summary>
    public void Match(Action onSuccess, Action<Error> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);

        if (IsSuccess)
        {
            onSuccess();
        }
        else
        {
            onFailure(Error);
        }
    }

    /// <summary>Chains an operation that may fail and produces no value, when this result is a success; failures are propagated unchanged.</summary>
    public Result Bind(Func<Result> next)
    {
        ArgumentNullException.ThrowIfNull(next);

        return IsSuccess
            ? next()
            : this;
    }

    /// <summary>Chains a value-producing operation that may fail, when this result is a success; failures are propagated unchanged.</summary>
    public Result<T> Bind<T>(Func<Result<T>> next)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(next);

        return IsSuccess
            ? next()
            : Result<T>.Failure(Error);
    }

    /// <summary>Transforms the error of a failed result; successes are propagated unchanged.</summary>
    public Result MapError(Func<Error, Error> mapper)
    {
        ArgumentNullException.ThrowIfNull(mapper);

        return IsSuccess
            ? this
            : Failure(mapper(Error));
    }

    /// <summary>Turns a successful result into a failure carrying <paramref name="error"/> when <paramref name="predicate"/> is not satisfied.</summary>
    /// <remarks>Failures are propagated unchanged and <paramref name="predicate"/> is not invoked.</remarks>
    public Result Ensure(Func<bool> predicate, Error error)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(error);

        return IsFailure || predicate()
            ? this
            : Failure(error);
    }

    /// <summary>Turns a successful result into a failure built by <paramref name="errorFactory"/> when <paramref name="predicate"/> is not satisfied.</summary>
    /// <remarks>Failures are propagated unchanged and neither delegate is invoked.</remarks>
    /// <exception cref="ArgumentNullException">A delegate is <see langword="null"/>, or <paramref name="errorFactory"/> returns <see langword="null"/>.</exception>
    public Result Ensure(Func<bool> predicate, Func<Error> errorFactory)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(errorFactory);

        return IsFailure || predicate()
            ? this
            : Failure(errorFactory());
    }

    /// <summary>Invokes a side effect when the result is a success, then returns the result unchanged.</summary>
    public Result Tap(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (IsSuccess)
        {
            action();
        }

        return this;
    }

    /// <summary>Invokes a side effect with the error when the result is a failure, then returns the result unchanged.</summary>
    public Result TapError(Action<Error> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (IsFailure)
        {
            action(Error);
        }

        return this;
    }

    /// <summary>Wraps <paramref name="error"/> in a failed result, so a method returning <see cref="Result"/> can <c>return error;</c>.</summary>
    public static implicit operator Result(Error error) => Failure(error);

    /// <inheritdoc/>
    public override string ToString() =>
        IsSuccess
            ? "Success"
            : $"Failure({Error})";
}