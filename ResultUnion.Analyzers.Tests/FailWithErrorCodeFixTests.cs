using ResultUnion.CodeFixes;
using static ResultUnion.Analyzers.Tests.CodeFixHarness;

namespace ResultUnion.Analyzers.Tests;

public sealed class FailWithErrorCodeFixTests
{
    private static Task<FixResult> Fix(string source) => FixAsync(source, new FailWithErrorCodeFix(), new ErrorAsValueAnalyzer());

    [Fact]
    public async Task Map_that_returns_an_error_becomes_Bind_with_a_failure_of_the_same_type()
    {
        var result = await Fix("""
            sealed record User(int Id);

            static class C
            {
                static readonly Error Missing = Error.NotFound("Missing.");
                static Error Blocked(int id) => Error.Forbidden($"User {id} is blocked.");

                static void M(Result<int> r, Result<User> user)
                {
                    var a = r.Map(_ => Missing);
                    var b = user.Map(u => Blocked(u.Id));
                    var c = r.Map<Error>(_ => Missing);
                }
            }
            """);

        Assert.Contains("var a = r.Bind(_ => Result<int>.Failure(Missing));", result.Code, StringComparison.Ordinal);
        Assert.Contains("var b = user.Bind(u => Result<User>.Failure(Blocked(u.Id)));", result.Code, StringComparison.Ordinal);
        Assert.Contains("var c = r.Bind(_ => Result<int>.Failure(Missing));", result.Code, StringComparison.Ordinal);   // type argument gone
        Assert.Equal([FailWithErrorCodeFix.Title, FailWithErrorCodeFix.Title, FailWithErrorCodeFix.Title], result.Titles);
    }

    [Fact]
    public async Task MapAsync_becomes_BindAsync_and_an_awaited_error_is_wrapped_inside_the_lambda()
    {
        var result = await Fix("""
            static class C
            {
                static readonly Error Missing = Error.NotFound("Missing.");
                static Task<Error> LoadErrorAsync(int n) => Task.FromResult(Missing);

                static async Task M(Result<int> r, Task<Result<int>> pending)
                {
                    var a = await pending.MapAsync(_ => Missing);
                    var b = await r.MapAsync(async n => await LoadErrorAsync(n));
                }
            }
            """);

        Assert.Contains("var a = await pending.BindAsync(_ => Result<int>.Failure(Missing));", result.Code, StringComparison.Ordinal);
        Assert.Contains("var b = await r.BindAsync(async n => Result<int>.Failure(await LoadErrorAsync(n)));", result.Code, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_tuple_keeps_its_tuple_type()
    {
        var result = await Fix("""
            static class C
            {
                static void M(Result<int> a, Result<string> b)
                {
                    var both = Result.CombineAll(a, b).Map((n, s) => Error.Conflict($"{n} {s}"));
                }
            }
            """);

        Assert.Contains("Result.CombineAll(a, b).Bind((n, s) => Result<(int, string)>.Failure(Error.Conflict($\"{n} {s}\")));", result.Code, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Written_types_generic_methods_tasks_of_errors_and_method_groups_get_no_fix()
    {
        var offered = await OfferedAsync("""
            static class C
            {
                static readonly Error Missing = Error.NotFound("Missing.");
                static Result<Error> _last;
                static Error Make(int n) => Missing;
                static Task<Error> LoadErrorAsync(int n) => Task.FromResult(Missing);
                static Result<T> Wrap<T>(T value) where T : notnull => Result<T>.Success(value);

                static async Task M(Result<int> r)
                {
                    var a = Wrap(Missing);
                    var b = await r.MapAsync(n => LoadErrorAsync(n));
                    var c = r.Map(Make);
                }
            }
            """, new FailWithErrorCodeFix(), new ErrorAsValueAnalyzer());

        Assert.Empty(offered);
    }
}
