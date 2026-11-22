using static ResultUnion.Analyzers.Tests.AnalyzerHarness;

namespace ResultUnion.Analyzers.Tests;

public sealed class CancellationAnalyzerTests
{
    private static Task<IReadOnlyList<(string Id, int Line)>> Analyze(string source) => AnalyzeAsync(source, new CancellationAnalyzer());

    [Fact]
    public async Task Returning_a_failure_from_a_cancellation_catch_is_reported()
    {
        var diagnostics = await Analyze("""
            class C
            {
                async Task<Result<int>> M(Func<Task<int>> work)
                {
                    try { return await work(); }
                    catch (OperationCanceledException) { return Error.Unavailable("Cancelled."); }
                }
            }
            """);

        Assert.Equal([("RU0005", 6)], diagnostics);
    }

    [Fact]
    public async Task TaskCanceledException_a_result_and_a_success_are_reported_too()
    {
        var diagnostics = await Analyze("""
            class C
            {
                async Task<Result> A(Func<Task> work)
                {
                    try { await work(); return Result.Success(); }
                    catch (TaskCanceledException ex) { return Result.Failure(Error.Unexpected(ex.Message)); }
                }

                async Task<Result> B(Func<Task> work)
                {
                    try { await work(); return Result.Success(); }
                    catch (OperationCanceledException) { return Result.Success(); }
                }

                Error D(Action work)
                {
                    try { work(); return Error.Validation("ok"); }
                    catch (OperationCanceledException) { return Error.Unexpected("Cancelled."); }
                }

                async Task<Result<int>> E(Func<Task<int>> work)
                {
                    try { return await work(); }
                    catch (OperationCanceledException) { return -1; }   // a fallback value becomes a success
                }
            }
            """);

        Assert.Equal([("RU0005", 6), ("RU0005", 12), ("RU0005", 18), ("RU0005", 24)], diagnostics);
    }

    [Fact]
    public async Task A_filtered_catch_for_a_timeout_is_fine()
    {
        var diagnostics = await Analyze("""
            class C
            {
                async Task<Result<int>> M(Func<CancellationToken, Task<int>> work, CancellationToken ct)
                {
                    try { return await work(ct); }
                    catch (TaskCanceledException) when (!ct.IsCancellationRequested) { return Error.Unavailable("Timed out."); }
                }
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Catches_that_rethrow_or_return_something_else_are_fine()
    {
        var diagnostics = await Analyze("""
            class C
            {
                async Task<Result<int>> A(Func<Task<int>> work)
                {
                    try { return await work(); }
                    catch (OperationCanceledException) { Console.WriteLine("cancelled"); throw; }
                }

                async Task<int> B(Func<Task<int>> work)
                {
                    try { return await work(); }
                    catch (OperationCanceledException) { return -1; }
                }

                async Task<Result<int>> D(Func<Task<int>> work)
                {
                    try { return await work(); }
                    catch (InvalidOperationException ex) { return Error.Unexpected(ex.Message); }
                }
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task A_lambda_inside_the_catch_does_not_count()
    {
        var diagnostics = await Analyze("""
            class C
            {
                void M(Action work, Action<Func<Result>> later)
                {
                    try { work(); }
                    catch (OperationCanceledException) { later(() => Result.Success()); throw; }
                }
            }
            """);

        Assert.Empty(diagnostics);
    }
}
