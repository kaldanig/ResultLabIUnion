using static ResultUnion.Analyzers.Tests.AnalyzerHarness;

namespace ResultUnion.Analyzers.Tests;

public sealed class ResultJsonAnalyzerTests
{
    // The ASP.NET Core and System.Net.Http.Json members the rule recognizes, by the names it looks for. The harness
    // references only the BCL and ResultUnion; the repository's Web API projects run the rule against the real ones.
    private const string Stubs = """

        namespace Microsoft.AspNetCore.Routing { public interface IEndpointRouteBuilder { } }

        namespace Microsoft.AspNetCore.Builder
        {
            public static class EndpointRouteBuilderExtensions
            {
                public static object MapGet(this Microsoft.AspNetCore.Routing.IEndpointRouteBuilder app, string pattern, System.Delegate handler) => null!;
                public static object MapPost(this Microsoft.AspNetCore.Routing.IEndpointRouteBuilder app, string pattern, System.Delegate handler) => null!;
            }
        }

        namespace Microsoft.AspNetCore.Http
        {
            public static class TypedResults { public static object Ok<T>(T value) => null!; }
        }

        namespace System.Net.Http.Json
        {
            public static class HttpClientJsonExtensions
            {
                public static System.Threading.Tasks.Task<T?> GetFromJsonAsync<T>(this System.Net.Http.HttpClient client, string uri) => null!;
            }
        }
        """;

    private static Task<IReadOnlyList<(string Id, int Line)>> Analyze(string source) =>
        AnalyzeAsync(source + Stubs, new ResultJsonAnalyzer());

    [Fact]
    public async Task Endpoint_handlers_that_return_a_result_are_reported()
    {
        var diagnostics = await Analyze("""
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Routing;

            static class Endpoints
            {
                static void Map(IEndpointRouteBuilder app)
                {
                    app.MapGet("/a", () => Result<int>.Success(1));
                    app.MapGet("/b", async () => await Task.FromResult(Result.Success()));
                    app.MapPost("/c", Handle);
                    app.MapGet("/fine", () => Handle().Result.Match(v => v, e => -1));
                }

                static Task<Result<int>> Handle() => Task.FromResult(Result<int>.Success(1));
            }
            """);

        Assert.Equal([("RU0010", 8), ("RU0010", 9), ("RU0010", 10)], diagnostics);
    }

    [Fact]
    public async Task Results_written_as_or_read_from_JSON_are_reported()
    {
        var diagnostics = await Analyze("""
            using System.Net.Http;
            using System.Net.Http.Json;
            using System.Text.Json;
            using Microsoft.AspNetCore.Http;

            static class C
            {
                static async Task M(Result<int> r, Result plain, HttpClient http)
                {
                    _ = JsonSerializer.Serialize(r);
                    _ = JsonSerializer.Serialize(plain);
                    _ = JsonSerializer.Deserialize<Result<int>>("1");
                    _ = TypedResults.Ok(r);
                    _ = await http.GetFromJsonAsync<Result<int>>("/a");
                }
            }
            """);

        Assert.Equal([("RU0010", 10), ("RU0010", 11), ("RU0010", 12), ("RU0010", 13), ("RU0010", 14)], diagnostics);
    }

    [Fact]
    public async Task Mapped_results_errors_and_other_calls_are_fine()
    {
        var diagnostics = await Analyze("""
            using System.Text.Json;
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http;
            using Microsoft.AspNetCore.Routing;

            static class C
            {
                static void M(IEndpointRouteBuilder app, Result<int> r)
                {
                    _ = JsonSerializer.Serialize(Error.NotFound("Missing."));              // an Error is a contract
                    _ = TypedResults.Ok(r.Match(value => value, error => -1));             // mapped first
                    app.MapGet("/value", () => r.Match(value => value.ToString(), error => error.Code));
                    _ = r.Map(value => value + 1);                                         // not JSON at all
                }
            }
            """);

        Assert.Empty(diagnostics);
    }
}
