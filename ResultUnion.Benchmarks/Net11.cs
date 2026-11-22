using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Jobs;

namespace ResultUnion.Benchmarks;

/// <summary>
/// The .NET 11 job. BenchmarkDotNet 0.15.8 knows runtimes only up to .NET 10 and fails out-of-process on net11.0
/// ("GetRuntimeVersion not implemented for NotRecognized"); 0.16 adds <see cref="CoreRuntime.Core11_0"/>.
/// </summary>
internal static class Net11
{
    /// <summary>Run each benchmark once instead of measuring it (<c>--smoke</c>); set before the benchmarks are read.</summary>
    public static bool Smoke { get; set; }

    public static Job Job
    {
        get
        {
            var job = Job.Default.WithRuntime(CoreRuntime.Core11_0).WithId("RuntimeAsync");
            return Smoke
                ? job.WithStrategy(RunStrategy.ColdStart).WithLaunchCount(1).WithWarmupCount(0).WithIterationCount(1)
                : job;
        }
    }
}
