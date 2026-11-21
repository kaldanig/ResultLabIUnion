using BenchmarkDotNet.Attributes;
using ResultUnion;

namespace ResultUnion.Benchmarks;

/// <summary>
/// Validating three fields at once. When all succeed, CombineAll builds a tuple; when they fail, it groups the errors
/// in one Validation error with Details (an allocation per failure is expected).
/// </summary>
public class CombineAllBenchmarks
{
    private static readonly Error Invalid = Error.Validation("Invalid.");

    private Result<int> _a, _b, _c;

    [Params(0, 1, 3)]
    public int Failures { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _a = Failures >= 1 ? Result<int>.Failure(Invalid.WithField("a")) : Result<int>.Success(1);
        _b = Failures >= 2 ? Result<int>.Failure(Invalid.WithField("b")) : Result<int>.Success(2);
        _c = Failures >= 3 ? Result<int>.Failure(Invalid.WithField("c")) : Result<int>.Success(3);
    }

    [Benchmark(Baseline = true, Description = "CombineAll(a, b, c).Map((x, y, z) => …)")]
    public int CombineAll() =>
        Result.CombineAll(_a, _b, _c)
            .Map(static (x, y, z) => x + y + z)
            .Match(static sum => sum, static e => -e.Details.Count);
}
