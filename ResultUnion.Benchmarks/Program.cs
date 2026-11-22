using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Running;
using ResultUnion.Benchmarks;

// Every benchmark reports allocated bytes: the point of a struct union is that the success path allocates nothing.
// The projects are built with .NET 11 Runtime Async (Directory.Build.targets); AsyncBenchmarks also runs a job built
// without it, to compare the two.
//
// --smoke runs every benchmark once (one launch, one iteration, no warm-up), to check that they still run; CI does it
// on every push (benchmarks.yml). The numbers it prints mean nothing.
Net11.Smoke = args.Contains("--smoke");
args = [.. args.Where(a => a != "--smoke")];

var config = DefaultConfig.Instance
    .AddJob(Net11.Job)
    .AddDiagnoser(MemoryDiagnoser.Default);

var summaries = (await BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).RunAsync(args, config)).ToList();

// BenchmarkDotNet reports a benchmark that throws, or a class it cannot run, in the log and the summary, but exits
// with 0; make it a failure, so CI notices.
var failed = summaries
    .Where(s => s.HasCriticalValidationErrors || s.Reports.Any(r => !r.Success))
    .Select(s => s.Title)
    .ToList();

foreach (var title in failed)
    await Console.Error.WriteLineAsync($"Failed: {title}");

return failed.Count == 0 ? 0 : 1;
