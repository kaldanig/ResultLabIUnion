using BenchmarkDotNet.Attributes;
using ResultUnion;

namespace ResultUnion.Benchmarks;

/// <summary>
/// The cost of a failure: returning a cached catalog error, building an error with an argument, and throwing and
/// catching an exception (the alternative the Result pattern replaces).
/// </summary>
public class FailureBenchmarks
{
    private static readonly Error Required =
        Error.Create("email.required", "Email is required.", ErrorType.Validation);

    private string _input = "bob@";

    [Benchmark(Baseline = true, Description = "return a cached catalog error")]
    public int CachedError() => Validate(_input, static _ => Required).Match(static _ => 0, static e => e.Code.Length);

    [Benchmark(Description = "return a new error with WithArgument")]
    public int ErrorWithArgument() =>
        Validate(_input, static value =>
                Error.Create("email.invalid_format", "Not a valid email.", ErrorType.Validation).WithArgument("value", value))
            .Match(static _ => 0, static e => e.Code.Length);

    [Benchmark(Description = "throw and catch an exception")]
    public int Exception()
    {
        try
        {
            return ValidateOrThrow(_input).Length;
        }
        catch (FormatException exception)
        {
            return exception.Message.Length;
        }
    }

    private static Result<string> Validate(string input, Func<string, Error> error) =>
        input.Contains('.') ? Result<string>.Success(input) : Result<string>.Failure(error(input));

    private static string ValidateOrThrow(string input) =>
        input.Contains('.') ? input : throw new FormatException($"'{input}' is not a valid email.");
}
