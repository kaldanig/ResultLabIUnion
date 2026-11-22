using static ResultUnion.Analyzers.Tests.AnalyzerHarness;

namespace ResultUnion.Analyzers.Tests;

public sealed class UncheckedAccessAnalyzerTests
{
    private static Task<IReadOnlyList<(string Id, int Line)>> Analyze(string body) => AnalyzeAsync($$"""
        class C
        {
            Result<int> Parse(string s) => Result<int>.Success(1);
            Result Validate() => Result.Success();
            Result<int> _field = Result<int>.Success(1);

            int M(Result<int> r, Result<int> other, Result plain)
            {
        {{body}}
                return 0;
            }
        }
        """, new UncheckedAccessAnalyzer());

    // The body starts on line 9 of the snippet.
    private const int FirstLine = 9;

    // ---- reported ----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Unchecked_reads_are_reported()
    {
        var diagnostics = await Analyze("""
                    var a = r.ResultValue;
                    var b = r.Error;
                    var c = plain.Error;
                    var d = Parse("1").ResultValue;
            """);

        Assert.Equal(
            [("RU0004", FirstLine), ("RU0004", FirstLine + 1), ("RU0004", FirstLine + 2), ("RU0004", FirstLine + 3)],
            diagnostics);
    }

    [Fact]
    public async Task The_wrong_branch_and_the_wrong_result_are_reported()
    {
        var diagnostics = await Analyze("""
                    if (r.IsSuccess) { var a = r.Error; }
                    if (r.IsFailure) { var b = r.ResultValue; }
                    if (other.IsSuccess) { var c = r.ResultValue; }
                    if (r.IsSuccess) { } else { var d = r.ResultValue; }
            """);

        Assert.Equal(
            [("RU0004", FirstLine), ("RU0004", FirstLine + 1), ("RU0004", FirstLine + 2), ("RU0004", FirstLine + 3)],
            diagnostics);
    }

    [Fact]
    public async Task A_call_result_is_reported_even_when_the_same_call_was_checked()
    {
        var diagnostics = await Analyze("""
                    if (Parse("1").IsSuccess) { var a = Parse("1").ResultValue; }
            """);

        Assert.Equal([("RU0004", FirstLine)], diagnostics);
    }

    [Fact]
    public async Task An_exit_that_does_not_always_exit_does_not_guard()
    {
        var diagnostics = await Analyze("""
                    if (r.IsFailure) { if (other.IsSuccess) return 1; }
                    var a = r.ResultValue;
            """);

        Assert.Equal([("RU0004", FirstLine + 1)], diagnostics);
    }

    // ---- not reported ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Reads_inside_a_matching_if_are_not_reported()
    {
        var diagnostics = await Analyze("""
                    if (r.IsSuccess) { var a = r.ResultValue; }
                    if (r.IsFailure) { var b = r.Error; }
                    if (!r.IsFailure) { var c = r.ResultValue; }
                    if (r.IsSuccess) { } else { var d = r.Error; }
                    if (r.IsSuccess == false) { var e = r.Error; }
                    if (plain.IsFailure) { var f = plain.Error; }
                    if (r.IsSuccess && other.IsSuccess) { var g = r.ResultValue + other.ResultValue; }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Conditional_and_logical_operators_guard_their_right_side()
    {
        var diagnostics = await Analyze("""
                    var a = r.IsSuccess ? r.ResultValue : -1;
                    var b = r.IsFailure ? r.Error.Code : "";
                    var c = r.IsSuccess && r.ResultValue > 0;
                    var d = r.IsFailure || r.ResultValue > 0;
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task An_earlier_exit_guards_the_rest_of_the_block()
    {
        var diagnostics = await Analyze("""
                    if (r.IsFailure) return -1;
                    if (!other.IsSuccess) { throw new InvalidOperationException(other.Error.Message); }
                    if (plain.IsSuccess) { } else { return 2; }
                    var a = r.ResultValue + other.ResultValue;
                    plain.Error?.ToString();
            """);

        // plain was established as a success, so reading its Error is the one mistake here.
        Assert.Equal([("RU0004", FirstLine + 4)], diagnostics);
    }

    [Fact]
    public async Task TryGet_methods_guard_like_IsSuccess()
    {
        var diagnostics = await Analyze("""
                    if (r.TryGetError(out var error)) return error.Code.Length;
                    var a = r.ResultValue;
                    if (other.TryGetResultValue(out var value)) { var b = other.ResultValue; } else { var c = other.Error; }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Fields_this_and_member_chains_are_tracked()
    {
        var diagnostics = await Analyze("""
                    if (_field.IsSuccess) { var a = _field.ResultValue; }
                    if (this._field.IsFailure) { var b = _field.Error; }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Code_inside_the_result_types_is_not_analyzed_and_other_members_are_ignored()
    {
        var diagnostics = await Analyze("""
                    var a = r.IsSuccess;
                    var b = r.Match(v => v, e => 0);
                    var c = r switch { int v => v, Error e => 0 };
            """);

        Assert.Empty(diagnostics);
    }

    // ---- the shapes of a check -----------------------------------------------------------------------------------------

    [Fact]
    public async Task Locals_are_tracked_like_parameters()
    {
        var diagnostics = await Analyze("""
                    var parsed = Parse("1");
                    if (parsed.IsFailure) return -1;
                    var a = parsed.ResultValue;
                    var later = Parse("2");
                    var b = later.ResultValue;
            """);

        Assert.Equal([("RU0004", FirstLine + 4)], diagnostics);
    }

    [Fact]
    public async Task Exits_through_both_branches_or_an_or_condition_guard_the_rest()
    {
        var diagnostics = await Analyze("""
                    if (r.IsFailure) { if (other.IsSuccess) return 1; else return 2; }
                    if (other.IsFailure || plain.IsFailure) return 3;
                    var a = r.ResultValue + other.ResultValue;
                    plain.Error?.ToString();
            """);

        // plain is known to be a success after the second line.
        Assert.Equal([("RU0004", FirstLine + 3)], diagnostics);
    }

    [Fact]
    public async Task Comparisons_with_a_literal_on_either_side_guard_and_others_do_not()
    {
        var diagnostics = await Analyze("""
                    if (false == r.IsSuccess) { var a = r.Error; }
                    if (true != r.IsFailure) { var b = r.ResultValue; }
                    if (r.IsSuccess == other.IsSuccess) { var c = r.ResultValue; }
            """);

        Assert.Equal([("RU0004", FirstLine + 2)], diagnostics);
    }

    [Fact]
    public async Task The_union_TryGetValue_overloads_guard_by_their_out_type()
    {
        var diagnostics = await Analyze("""
                    if (r.TryGetValue(out int value)) { var a = r.ResultValue; }
                    if (r.TryGetValue(out Error? error)) { var b = r.Error; }
                    if (other.TryGetValue(out Error? failure)) { var c = other.ResultValue; }
            """);

        Assert.Equal([("RU0004", FirstLine + 2)], diagnostics);
    }

    [Fact]
    public async Task Properties_and_static_members_are_tracked()
    {
        var diagnostics = await AnalyzeAsync("""
            class C
            {
                static Result<int> Shared = Result<int>.Success(1);
                Result<int> Current => Result<int>.Success(1);

                int M()
                {
                    if (Shared.IsFailure) return -1;
                    if (Current.IsSuccess) { return Current.ResultValue + Shared.ResultValue; }
                    return C.Shared.ResultValue + Current.ResultValue;
                }
            }
            """, new UncheckedAccessAnalyzer());

        // Current is known to be a failure on the last line; Shared, as C.Shared too, a success.
        Assert.Equal([("RU0004", 10)], diagnostics);
    }
}
