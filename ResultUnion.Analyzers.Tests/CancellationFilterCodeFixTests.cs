using ResultUnion.CodeFixes;
using static ResultUnion.Analyzers.Tests.CodeFixHarness;

namespace ResultUnion.Analyzers.Tests;

public sealed class CancellationFilterCodeFixTests
{
    [Fact]
    public async Task A_catch_with_a_token_in_scope_gets_the_filter()
    {
        var result = await FixAsync("""
            class C
            {
                async Task<Result<int>> M(Func<CancellationToken, Task<int>> work, CancellationToken ct)
                {
                    try { return await work(ct); }
                    catch (TaskCanceledException) { return Error.Unavailable("Timed out."); }
                }
            }
            """, new CancellationFilterCodeFix(), new CancellationAnalyzer());

        Assert.Contains(
            """catch (TaskCanceledException) when (!ct.IsCancellationRequested) { return Error.Unavailable("Timed out."); }""",
            result.Code, StringComparison.Ordinal);
        Assert.Equal(["Let cancellation of 'ct' propagate (when (!ct.IsCancellationRequested))"], result.Titles);
    }

    [Fact]
    public async Task The_brace_on_its_own_line_stays_there()
    {
        var result = await FixAsync("""
            class C
            {
                async Task<Result<int>> M(Func<CancellationToken, Task<int>> work, CancellationToken cancellationToken)
                {
                    try
                    {
                        return await work(cancellationToken);
                    }
                    catch (OperationCanceledException ex)
                    {
                        return Error.Unexpected(ex.Message);
                    }
                }
            }
            """, new CancellationFilterCodeFix(), new CancellationAnalyzer());

        Assert.Contains(
            "catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)\n        {\n            return Error.Unexpected(ex.Message);",
            result.Code.ReplaceLineEndings("\n"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Each_token_in_scope_gets_its_own_fix()
    {
        var offered = await OfferedAsync("""
            class C
            {
                async Task<Result<int>> M(Func<CancellationToken, Task<int>> work, CancellationToken ct)
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    var linked = timeout.Token;
                    try { return await work(linked); }
                    catch (OperationCanceledException) { return Error.Unavailable("Timed out."); }
                }
            }
            """, new CancellationFilterCodeFix(), new CancellationAnalyzer());

        Assert.Equal(
            ["Let cancellation of 'linked' propagate (when (!linked.IsCancellationRequested))",
             "Let cancellation of 'ct' propagate (when (!ct.IsCancellationRequested))"],
            offered);
    }

    [Fact]
    public async Task Without_a_token_there_is_no_fix()
    {
        var offered = await OfferedAsync("""
            class C
            {
                async Task<Result<int>> M(Func<Task<int>> work)
                {
                    try { return await work(); }
                    catch (OperationCanceledException) { return Error.Unavailable("Cancelled."); }
                }
            }
            """, new CancellationFilterCodeFix(), new CancellationAnalyzer());

        Assert.Empty(offered);
    }
}
