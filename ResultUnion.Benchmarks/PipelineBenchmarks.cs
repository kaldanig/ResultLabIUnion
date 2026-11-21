using BenchmarkDotNet.Attributes;
using ResultUnion;

namespace ResultUnion.Benchmarks;

/// <summary>
/// The same four-step validation written as a Result pipeline and as plain imperative code with early returns.
/// The pipeline's lambdas are static (no captures), so the difference is the delegate calls and struct copies.
/// </summary>
public class PipelineBenchmarks
{
    private static readonly Error Empty = Error.Create("input.empty", "Input is empty.", ErrorType.Validation);
    private static readonly Error NotANumber = Error.Create("input.not_a_number", "Not a number.", ErrorType.Validation);
    private static readonly Error OutOfRange = Error.Create("input.out_of_range", "Out of range.", ErrorType.Validation);

    [Params("42", "abc")]
    public string Input { get; set; } = "";

    [Benchmark(Baseline = true, Description = "imperative, if + return")]
    public int Imperative()
    {
        if (string.IsNullOrWhiteSpace(Input))
            return -Empty.Code.Length;
        if (!int.TryParse(Input, out var number))
            return -NotANumber.Code.Length;
        if (number is < 0 or > 100)
            return -OutOfRange.Code.Length;
        return number * 2;
    }

    [Benchmark(Description = "Ensure → Bind → Ensure → Map → Match")]
    public int Pipeline() =>
        Result<string>.Success(Input)
            .Ensure(static s => !string.IsNullOrWhiteSpace(s), Empty)
            .Bind(static s => int.TryParse(s, out var n) ? Result<int>.Success(n) : Result<int>.Failure(NotANumber))
            .Ensure(static n => n is >= 0 and <= 100, OutOfRange)
            .Map(static n => n * 2)
            .Match(static n => n, static e => -e.Code.Length);
}
