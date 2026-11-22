using static ResultUnion.Analyzers.Tests.AnalyzerHarness;

namespace ResultUnion.Analyzers.Tests;

public sealed class ErrorAsValueAnalyzerTests
{
    private static Task<IReadOnlyList<(string Id, int Line)>> Analyze(string source) => AnalyzeAsync(source, new ErrorAsValueAnalyzer());

    [Fact]
    public async Task Result_of_Error_written_out_is_reported()
    {
        var diagnostics = await Analyze("""
            class C
            {
                Result<Error> _last;
                Result<Error> Find(Result<Error> previous) => previous;
                Task<Result<Error>> FindAsync() => Task.FromResult(Result<Error>.Success(Error.NotFound("Missing.")));
                object Default() => default(Result<Error>);
            }
            """);

        Assert.Equal([("RU0009", 3), ("RU0009", 4), ("RU0009", 4), ("RU0009", 5), ("RU0009", 5), ("RU0009", 6)], diagnostics);
    }

    [Fact]
    public async Task A_call_that_produces_it_is_reported_once_per_chain()
    {
        var diagnostics = await Analyze("""
            class C
            {
                static readonly Error Missing = Error.NotFound("Missing.");

                async Task M(Result<int> r, Task<Result<int>> pending)
                {
                    var mapped = r.Map(_ => Missing);
                    var chained = r.Map(_ => Missing).Tap(_ => { }).Ensure(_ => true, Missing);
                    var awaited = await pending.MapAsync(_ => Missing);
                    var wrapped = Wrap(Missing);
                    var fine = r.Bind(_ => Result<int>.Failure(Missing));
                }

                static Result<T> Wrap<T>(T value) where T : notnull => Result<T>.Success(value);
            }
            """);

        // Line 14 declares Wrap<T>, which is generic code and fine; Wrap(Missing) on line 10 closes it over Error.
        Assert.Equal([("RU0009", 7), ("RU0009", 8), ("RU0009", 9), ("RU0009", 10)], diagnostics);
    }

    [Fact]
    public async Task Results_of_other_types_and_errors_as_failures_are_fine()
    {
        var diagnostics = await Analyze("""
            class C
            {
                Result<int> Parse(string s) => s.Length > 0 ? 1 : Error.Validation("Required.");
                Result<string> Describe(Result<int> r) => r.Map(n => n.ToString()).MapError(e => e.WithField("n"));
                Result<Error[]> Collect() => Result<Error[]>.Success([]);
                Error Pick(Result<int> r) => r.Match(_ => Error.Validation("None."), e => e);
            }
            """);

        Assert.Empty(diagnostics);
    }
}
