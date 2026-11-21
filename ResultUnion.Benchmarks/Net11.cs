using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Jobs;

namespace ResultUnion.Benchmarks;

/// <summary>
/// The .NET 11 job. BenchmarkDotNet 0.15.8 knows runtimes only up to .NET 10 and fails out-of-process on net11.0
/// ("GetRuntimeVersion not implemented for NotRecognized"); 0.16 adds <see cref="CoreRuntime.Core11_0"/>.
/// </summary>
internal static class Net11
{
    public static Job Job { get; } = Job.Default.WithRuntime(CoreRuntime.Core11_0).WithId("RuntimeAsync");
}
