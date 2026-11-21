using BenchmarkDotNet.Attributes;
using ResultUnion;

namespace ResultUnion.Benchmarks;

/// <summary>
/// Async chains whose I/O completes synchronously (a cache hit), so the numbers show the library's own overhead.
/// A Task object exists wherever it is a value: the source of each next step, and every async delegate's return.
/// Steps that keep the result (Ensure, Tap, TapError) return their source task, so they add nothing.
/// Runs with and without .NET 11 Runtime Async.
/// </summary>
[RuntimeAsyncComparison]
public class AsyncBenchmarks
{
    private static readonly Error NotFound = Error.Create("user.not_found", "Not found.", ErrorType.NotFound);
    private static readonly Error Inactive = Error.Create("user.inactive", "Inactive.", ErrorType.Conflict);

    private int _id = 7;

    [Benchmark(Baseline = true, Description = "hand-written async with if checks")]
    public async Task<int> HandWritten()
    {
        var user = await FindAsync(_id);
        if (user is null)
            return -1;
        var orders = await CountOrdersAsync(user.Value);
        return orders * 2;
    }

    [Benchmark(Description = "ToResultAsync → BindAsync → MapAsync")]
    public async Task<int> Chain() =>
        (await FindAsync(_id)
            .ToResultAsync(NotFound)
            .BindAsync(static async user => Result<int>.Success(await CountOrdersAsync(user)))
            .MapAsync(static orders => orders * 2))
        .Match(static n => n, static _ => -1);

    [Benchmark(Description = "handler: ToResultAsync → EnsureAsync → TapAsync → MapAsync → TapErrorAsync")]
    public async Task<int> Handler() =>
        (await FindAsync(_id)
            .ToResultAsync(NotFound)
            .EnsureAsync(static user => user > 0, Inactive)
            .TapAsync(static _ => { })
            .MapAsync(static user => user * 2)
            .TapErrorAsync(static _ => { }))
        .Match(static n => n, static _ => -1);

    private static Task<int?> FindAsync(int id) => Task.FromResult<int?>(id);

    private static Task<int> CountOrdersAsync(int user) => Task.FromResult(user * 3);
}
