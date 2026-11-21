using BenchmarkDotNet.Attributes;
using ResultUnion;

namespace ResultUnion.Benchmarks;

/// <summary>
/// Reading a <c>Result&lt;int&gt;</c>. A switch on the union compiles to the non-boxing <c>TryGetValue</c> pattern,
/// so it should allocate nothing; the union's <c>Value</c> property is an <c>object?</c> and boxes the int.
/// </summary>
public class MatchingBenchmarks
{
    private static readonly Error Failure = Error.Validation("Invalid.");

    private Result<int> _success;
    private Result<int> _failure;

    [Params(true, false)]
    public bool IsSuccess { get; set; }

    private Result<int> Current => IsSuccess ? _success : _failure;

    [GlobalSetup]
    public void Setup()
    {
        _success = Result<int>.Success(42);
        _failure = Result<int>.Failure(Failure);
    }

    [Benchmark(Baseline = true, Description = "switch on the union")]
    public int Switch() => Current switch
    {
        int value => value,
        Error error => error.Code.Length,
    };

    [Benchmark(Description = "Match(onSuccess, onFailure)")]
    public int Match() => Current.Match(value => value, error => error.Code.Length);

    [Benchmark(Description = "TryGetResultValue / TryGetError")]
    public int TryGet()
    {
        var result = Current;
        if (result.TryGetResultValue(out var value))
            return value;
        return result.TryGetError(out var error) ? error.Code.Length : 0;
    }

    [Benchmark(Description = "IsSuccess + ResultValue / Error")]
    public int Properties()
    {
        var result = Current;
        return result.IsSuccess ? result.ResultValue : result.Error.Code.Length;
    }

    [Benchmark(Description = "Value is int (object?, boxes)")]
    public int ValueProperty() => Current.Value is int value ? value : ((Error)Current.Value!).Code.Length;
}
