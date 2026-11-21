using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Running;
using ResultUnion.Benchmarks;

// Every benchmark reports allocated bytes: the point of a struct union is that the success path allocates nothing.
// The projects are built with .NET 11 Runtime Async (Directory.Build.targets); AsyncBenchmarks also runs a job built
// without it, to compare the two.
var config = DefaultConfig.Instance
    .AddJob(Net11.Job)
    .AddDiagnoser(MemoryDiagnoser.Default);

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, config);
