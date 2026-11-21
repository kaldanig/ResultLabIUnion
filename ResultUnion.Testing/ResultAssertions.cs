using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace ResultUnion.Testing;

/// <summary>
/// Assertions for tests of code that returns <see cref="Result"/> and <see cref="Result{T}"/>: a success returns its
/// value, a failure returns its <see cref="Error"/> for further checks, and anything else throws
/// <see cref="ResultAssertionException"/> with the whole result in the message.
/// </summary>
/// <remarks>
/// <para>
/// Assert on what a client depends on: the code, the <see cref="ErrorType"/>, the field and the arguments, not the
/// message, which is text for people and may change.
/// </para>
/// <code>
/// var email = Email.Create("ann@example.com").ShouldSucceed();
/// Email.Create("").ShouldFail("email.required").ShouldHaveType(ErrorType.Validation);
/// await service.RegisterAsync(command, ct).ShouldFailAsync("user.email_taken");
/// </code>
/// </remarks>
public static class ResultAssertions
{
    private const string RS0026 = "Different receivers in one static class, not real overloads.";

    /// <typeparam name="T">The type of the success value.</typeparam>
    /// <param name="result">The result to check.</param>
    extension<T>(Result<T> result)
        where T : notnull
    {
        /// <summary>Asserts that the result is a success and returns its value.</summary>
        /// <exception cref="ResultAssertionException">The result is a failure.</exception>
        [SuppressMessage("ApiDesign", "RS0026", Justification = RS0026)]
        public T ShouldSucceed()
        {
            if (result.TryGetError(out var error))
                throw new ResultAssertionException($"Expected a success, but the result failed with {Describe(error)}.");

            return result.TryGetResultValue(out var value) ? value : throw new UnreachableException();
        }

        /// <summary>Asserts that the result is a failure and returns its error.</summary>
        /// <exception cref="ResultAssertionException">The result is a success.</exception>
        [SuppressMessage("ApiDesign", "RS0026", Justification = RS0026)]
        public Error ShouldFail()
        {
            if (result.TryGetError(out var error))
                return error;

            var value = result.TryGetResultValue(out var success) ? (object)success : null;
            throw new ResultAssertionException($"Expected a failure, but the result succeeded with {Value(value)}.");
        }

        /// <summary>Asserts that the result is a failure with the error code <paramref name="code"/> and returns the error.</summary>
        /// <exception cref="ArgumentException"><paramref name="code"/> is <see langword="null"/>, empty or white space.</exception>
        /// <exception cref="ResultAssertionException">The result is a success, or fails with another code.</exception>
        [SuppressMessage("ApiDesign", "RS0026", Justification = RS0026)]
        public Error ShouldFail(string code)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(code);

            return result.ShouldFail().ShouldHaveCode(code);
        }
    }

    /// <param name="result">The result to check.</param>
    extension(Result result)
    {
        /// <summary>Asserts that the result is a success.</summary>
        /// <exception cref="ResultAssertionException">The result is a failure.</exception>
        [SuppressMessage("ApiDesign", "RS0026", Justification = RS0026)]
        public void ShouldSucceed()
        {
            if (result.TryGetError(out var error))
                throw new ResultAssertionException($"Expected a success, but the result failed with {Describe(error)}.");
        }

        /// <summary>Asserts that the result is a failure and returns its error.</summary>
        /// <exception cref="ResultAssertionException">The result is a success.</exception>
        [SuppressMessage("ApiDesign", "RS0026", Justification = RS0026)]
        public Error ShouldFail() =>
            result.TryGetError(out var error)
                ? error
                : throw new ResultAssertionException("Expected a failure, but the result succeeded.");

        /// <summary>Asserts that the result is a failure with the error code <paramref name="code"/> and returns the error.</summary>
        /// <exception cref="ArgumentException"><paramref name="code"/> is <see langword="null"/>, empty or white space.</exception>
        /// <exception cref="ResultAssertionException">The result is a success, or fails with another code.</exception>
        [SuppressMessage("ApiDesign", "RS0026", Justification = RS0026)]
        public Error ShouldFail(string code)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(code);

            return result.ShouldFail().ShouldHaveCode(code);
        }
    }

    /// <typeparam name="T">The type of the success value.</typeparam>
    /// <param name="source">The pending result to check.</param>
    extension<T>(Task<Result<T>> source)
        where T : notnull
    {
        /// <summary>Awaits the result, asserts that it is a success and returns its value.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
        [SuppressMessage("ApiDesign", "RS0026", Justification = RS0026)]
        public Task<T> ShouldSucceedAsync()
        {
            ArgumentNullException.ThrowIfNull(source);

            return Core(source);

            static async Task<T> Core(Task<Result<T>> source) => (await source.ConfigureAwait(false)).ShouldSucceed();
        }

        /// <summary>Awaits the result, asserts that it is a failure and returns its error.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
        [SuppressMessage("ApiDesign", "RS0026", Justification = RS0026)]
        public Task<Error> ShouldFailAsync()
        {
            ArgumentNullException.ThrowIfNull(source);

            return Core(source);

            static async Task<Error> Core(Task<Result<T>> source) => (await source.ConfigureAwait(false)).ShouldFail();
        }

        /// <summary>Awaits the result, asserts that it is a failure with the error code <paramref name="code"/> and returns the error.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="code"/> is <see langword="null"/>, empty or white space.</exception>
        [SuppressMessage("ApiDesign", "RS0026", Justification = RS0026)]
        public Task<Error> ShouldFailAsync(string code)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentException.ThrowIfNullOrWhiteSpace(code);

            return Core(source, code);

            static async Task<Error> Core(Task<Result<T>> source, string code) =>
                (await source.ConfigureAwait(false)).ShouldFail(code);
        }
    }

    /// <param name="source">The pending result to check.</param>
    extension(Task<Result> source)
    {
        /// <summary>Awaits the result and asserts that it is a success.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
        [SuppressMessage("ApiDesign", "RS0026", Justification = RS0026)]
        public Task ShouldSucceedAsync()
        {
            ArgumentNullException.ThrowIfNull(source);

            return Core(source);

            static async Task Core(Task<Result> source) => (await source.ConfigureAwait(false)).ShouldSucceed();
        }

        /// <summary>Awaits the result, asserts that it is a failure and returns its error.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
        [SuppressMessage("ApiDesign", "RS0026", Justification = RS0026)]
        public Task<Error> ShouldFailAsync()
        {
            ArgumentNullException.ThrowIfNull(source);

            return Core(source);

            static async Task<Error> Core(Task<Result> source) => (await source.ConfigureAwait(false)).ShouldFail();
        }

        /// <summary>Awaits the result, asserts that it is a failure with the error code <paramref name="code"/> and returns the error.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="code"/> is <see langword="null"/>, empty or white space.</exception>
        [SuppressMessage("ApiDesign", "RS0026", Justification = RS0026)]
        public Task<Error> ShouldFailAsync(string code)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentException.ThrowIfNullOrWhiteSpace(code);

            return Core(source, code);

            static async Task<Error> Core(Task<Result> source, string code) => (await source.ConfigureAwait(false)).ShouldFail(code);
        }
    }

    /// <param name="error">The error to check.</param>
    extension(Error error)
    {
        /// <summary>Asserts that the error has the code <paramref name="code"/>; returns the error for further checks.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="error"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="code"/> is <see langword="null"/>, empty or white space.</exception>
        /// <exception cref="ResultAssertionException">The code is different.</exception>
        public Error ShouldHaveCode(string code)
        {
            ArgumentNullException.ThrowIfNull(error);
            ArgumentException.ThrowIfNullOrWhiteSpace(code);

            return error.Code == code
                ? error
                : throw new ResultAssertionException($"Expected the error code '{code}', but the error is {Describe(error)}.");
        }

        /// <summary>Asserts that the error has the category <paramref name="type"/>; returns the error for further checks.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="error"/> is <see langword="null"/>.</exception>
        /// <exception cref="ResultAssertionException">The category is different.</exception>
        public Error ShouldHaveType(ErrorType type)
        {
            ArgumentNullException.ThrowIfNull(error);

            return error.Type == type
                ? error
                : throw new ResultAssertionException($"Expected an error of type {type}, but the error is {Describe(error)}.");
        }

        /// <summary>Asserts that the error is about the field <paramref name="field"/> (<see langword="null"/>: about no field); returns the error for further checks.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="error"/> is <see langword="null"/>.</exception>
        /// <exception cref="ResultAssertionException">The field is different.</exception>
        public Error ShouldHaveField(string? field)
        {
            ArgumentNullException.ThrowIfNull(error);

            return error.Field == field
                ? error
                : throw new ResultAssertionException(
                    $"Expected {(field is null ? "no field" : $"the field '{field}'")}, but the error is {Describe(error)}.");
        }

        /// <summary>Asserts that the error has the argument <paramref name="name"/> with <paramref name="value"/>; numbers compare by value, so <c>5</c> equals <c>5L</c> and <c>5.0</c>. Returns the error for further checks.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="error"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="name"/> is <see langword="null"/>, empty or white space.</exception>
        /// <exception cref="ResultAssertionException">The argument is missing or has another value.</exception>
        public Error ShouldHaveArgument(string name, object? value)
        {
            ArgumentNullException.ThrowIfNull(error);
            ArgumentException.ThrowIfNullOrWhiteSpace(name);

            if (!error.Arguments.TryGetValue(name, out var actual))
                throw new ResultAssertionException($"Expected the argument '{name}', but the error has {Arguments(error)}: {Describe(error)}.");

            return SameValue(actual, value)
                ? error
                : throw new ResultAssertionException($"Expected the argument '{name}' to be {Value(value)}, but it is {Value(actual)}.");
        }

        /// <summary>
        /// Asserts that the error reports exactly these field errors, in any order: the innermost errors
        /// (<see cref="Error.Flatten"/>), each as its field and code. Returns the error for further checks.
        /// </summary>
        /// <remarks>A single error without details counts as one field error, so the same assertion works for one or several.</remarks>
        /// <exception cref="ArgumentNullException"><paramref name="error"/> is <see langword="null"/>.</exception>
        /// <exception cref="ResultAssertionException">A field error is missing, or there is one more than expected.</exception>
        public Error ShouldHaveFieldErrors(params IEnumerable<(string? Field, string Code)> expected)
        {
            ArgumentNullException.ThrowIfNull(error);
            ArgumentNullException.ThrowIfNull(expected);

            var wanted = expected.Select(Pair).Order(StringComparer.Ordinal).ToList();
            var actual = error.Flatten().Select(e => Pair((e.Field, e.Code))).Order(StringComparer.Ordinal).ToList();
            if (wanted.SequenceEqual(actual, StringComparer.Ordinal))
                return error;

            var missing = wanted.Except(actual, StringComparer.Ordinal).ToList();
            var extra = actual.Except(wanted, StringComparer.Ordinal).ToList();
            throw new ResultAssertionException(
                "The field errors are not the expected ones." +
                (missing.Count > 0 ? $" Missing: {string.Join(", ", missing)}." : "") +
                (extra.Count > 0 ? $" Not expected: {string.Join(", ", extra)}." : "") +
                $" The error is {Describe(error)}.");

            static string Pair((string? Field, string Code) entry) => $"{entry.Field ?? "(no field)"} → {entry.Code}";
        }
    }

    private static string Describe(Error error) => $"{error} ({error.Type}{(error.Arguments.Count > 0 ? $", {Arguments(error)}" : "")})";

    private static string Arguments(Error error) =>
        error.Arguments.Count == 0
            ? "no arguments"
            : "arguments " + string.Join(", ", error.Arguments.OrderBy(a => a.Key, StringComparer.Ordinal).Select(a => $"{a.Key}={Value(a.Value)}"));

    private static string Value(object? value) => value switch
    {
        null => "null",
        string text => $"\"{text}\"",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? value.GetType().Name,
    };

    private static bool SameValue(object? actual, object? expected) =>
        Equals(actual, expected) || (AsDecimal(actual) is { } left && AsDecimal(expected) is { } right && left == right);

    private static decimal? AsDecimal(object? value) => value switch
    {
        int or long or short or byte or sbyte or uint or ulong or ushort or decimal => Convert.ToDecimal(value, CultureInfo.InvariantCulture),
        double d when !double.IsNaN(d) && !double.IsInfinity(d) && Math.Abs(d) < 7.9e28 => (decimal)d,
        float f when !float.IsNaN(f) && !float.IsInfinity(f) && Math.Abs(f) < 7.9e28f => (decimal)f,
        _ => null,
    };
}
