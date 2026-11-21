using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;

namespace ResultUnion.Benchmarks;

/// <summary>
/// Adds a job built with the classic compiler-generated state machines (<c>-p:EnableRuntimeAsync=false</c>, which also
/// applies to the referenced ResultUnion project) next to the global <c>RuntimeAsync</c> job, so a benchmark class
/// runs both ways.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class RuntimeAsyncComparisonAttribute : Attribute, IConfigSource
{
    public RuntimeAsyncComparisonAttribute()
    {
        Config = ManualConfig.CreateEmpty()
            .AddJob(Net11.Job.WithId("StateMachines")
                .WithArguments([new MsBuildArgument("/p:EnableRuntimeAsync=false")]));
    }

    public IConfig Config { get; }
}
