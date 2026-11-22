using ResultUnion.CodeFixes;
using static ResultUnion.Analyzers.Tests.CodeFixHarness;

namespace ResultUnion.Analyzers.Tests;

public sealed class ToHttpResultCodeFixTests
{
    // ResultUnion.AspNetCore's extensions, by the name the fix looks for (the harness does not reference the package).
    private const string AspNetCore = """

        namespace ResultUnion.AspNetCore
        {
            public static class ResultHttpExtensions
            {
                public static object ToHttpResult<T>(this ResultUnion.Result<T> result) where T : notnull => null!;
                public static object ToHttpResult(this ResultUnion.Result result) => null!;
                public static System.Threading.Tasks.Task<object> ToHttpResultAsync<T>(this System.Threading.Tasks.Task<ResultUnion.Result<T>> source) where T : notnull => null!;
                public static System.Threading.Tasks.Task<object> ToHttpResultAsync(this System.Threading.Tasks.Task<ResultUnion.Result> source) => null!;
            }
        }
        """;

    private const string Endpoints = """
        using Microsoft.AspNetCore.Builder;
        using Microsoft.AspNetCore.Routing;

        static class Endpoints
        {
            static Result<int> Find(int id) => Result<int>.Success(id);
            static Task<Result<int>> FindAsync(int id) => Task.FromResult(Result<int>.Success(id));
            static Task<Result> DeleteAsync(int id) => Task.FromResult(Result.Success());

            static void Map(IEndpointRouteBuilder app)
            {
        """;

    private static Task<FixResult> Fix(string body, string? extra = null) =>
        FixAsync(Endpoints + body + "\n    }\n}" + (extra ?? "") + AspNetCore + ResultJsonAnalyzerTests.Stubs,
            new ToHttpResultCodeFix(), new ResultJsonAnalyzer());

    private static string Body(FixResult result) =>
        result.Code[(result.Code.IndexOf("static void Map(", StringComparison.Ordinal))..result.Code.IndexOf("\n    }\n}", StringComparison.Ordinal)];

    [Fact]
    public async Task A_handler_returning_a_result_or_a_task_returns_its_response()
    {
        var result = await Fix("""
                    app.MapGet("/a/{id}", (int id) => Find(id));
                    app.MapGet("/b/{id}", (int id) => FindAsync(id));
                    app.MapPost("/c/{id}", (int id) => DeleteAsync(id));
            """);

        var body = Body(result);
        Assert.Contains("""app.MapGet("/a/{id}", (int id) => Find(id).ToHttpResult());""", body, StringComparison.Ordinal);
        Assert.Contains("""app.MapGet("/b/{id}", (int id) => FindAsync(id).ToHttpResultAsync());""", body, StringComparison.Ordinal);
        Assert.Contains("""app.MapPost("/c/{id}", (int id) => DeleteAsync(id).ToHttpResultAsync());""", body, StringComparison.Ordinal);
        Assert.Equal(["Return ToHttpResult()", "Return ToHttpResultAsync()", "Return ToHttpResultAsync()"], result.Titles);
    }

    [Fact]
    public async Task An_awaited_or_conditional_body_is_parenthesized()
    {
        var result = await Fix("""
                    app.MapGet("/a/{id}", async (int id) => await FindAsync(id));
                    app.MapGet("/b/{id}", (int id) => id > 0 ? Find(id) : Result<int>.Failure(Error.Validation("Positive.")));
            """);

        var body = Body(result);
        Assert.Contains("""async (int id) => (await FindAsync(id)).ToHttpResult());""", body, StringComparison.Ordinal);
        Assert.Contains("""(int id) => (id > 0 ? Find(id) : Result<int>.Failure(Error.Validation("Positive."))).ToHttpResult());""", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_using_is_added_once_and_only_when_missing()
    {
        var added = await Fix("""
                    app.MapGet("/a/{id}", (int id) => Find(id));
                    app.MapGet("/b/{id}", (int id) => Find(id));
            """);
        var present = await FixAsync("using ResultUnion.AspNetCore;\n" + Endpoints
            + """
                    app.MapGet("/a/{id}", (int id) => Find(id));
                }
            }
            """ + AspNetCore + ResultJsonAnalyzerTests.Stubs, new ToHttpResultCodeFix(), new ResultJsonAnalyzer());

        Assert.Single(added.Code.Split('\n'), line => line.Trim() == "using ResultUnion.AspNetCore;");
        Assert.Single(present.Code.Split('\n'), line => line.Trim() == "using ResultUnion.AspNetCore;");
    }

    [Fact]
    public async Task Block_bodies_method_groups_and_JSON_calls_get_no_fix()
    {
        var offered = await OfferedAsync(Endpoints + """
                    app.MapGet("/a/{id}", (int id) => { return Find(id); });
                    app.MapGet("/b/{id}", Find);
                    _ = System.Text.Json.JsonSerializer.Serialize(Find(1));
                }
            }
            """ + AspNetCore + ResultJsonAnalyzerTests.Stubs, new ToHttpResultCodeFix(), new ResultJsonAnalyzer());

        Assert.Empty(offered);
    }

    [Fact]
    public async Task Without_ResultUnion_AspNetCore_there_is_no_fix()
    {
        var offered = await OfferedAsync(Endpoints + """
                    app.MapGet("/a/{id}", (int id) => Find(id));
                }
            }
            """ + ResultJsonAnalyzerTests.Stubs, new ToHttpResultCodeFix(), new ResultJsonAnalyzer());

        Assert.Empty(offered);
    }
}
