using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ResultUnion.Articles.Tests;

/// <summary>
/// Keeps docs/articles honest: every line of C# an article shows must appear in this project or in ResultUnion.Articles,
/// where it compiles (with the analyzers) and, for the testing article, runs. An article edited without its sample, or
/// a sample that drifted from its article, fails here.
/// </summary>
public sealed partial class ArticleCodeTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly string Root = FindRoot();

    public static TheoryData<string> Articles =>
        [.. Directory.GetFiles(Path.Combine(Root, "docs", "articles"), "*.md").Select(path => Path.GetFileName(path)).Order(StringComparer.Ordinal)];

    [Theory]
    [MemberData(nameof(Articles))]
    public void Every_line_of_CSharp_in_the_article_is_in_a_compiled_sample(string article)
    {
        var sources = Normalize(string.Join("\n",
            new[] { "ResultUnion.Articles", "ResultUnion.Articles.Tests" }
                .SelectMany(project => Directory.GetFiles(Path.Combine(Root, project), "*.cs", SearchOption.AllDirectories))
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                .Select(File.ReadAllText)));

        var markdown = File.ReadAllText(Path.Combine(Root, "docs", "articles", article)).ReplaceLineEndings("\n");
        var missing = CSharpBlock().Matches(markdown)
            .SelectMany(block => block.Groups["code"].Value.Split('\n'))
            .Where(line => !string.IsNullOrWhiteSpace(line) && !sources.Contains(Normalize(line), StringComparison.Ordinal))
            .Select(line => line.Trim())
            .ToList();

        Assert.True(missing.Count == 0,
            $"{article} shows C# that is not in ResultUnion.Articles or ResultUnion.Articles.Tests, so nothing compiles it:\n" +
            string.Join("\n", missing));
    }

    [Fact]
    public void The_articles_are_found() => Assert.True(Articles.Count() >= 4, $"Only {Articles.Count()} articles were found under {Root}.");

    [Fact]
    public async Task The_problem_response_in_Getting_started_is_the_real_one()
    {
        var markdown = File.ReadAllText(Path.Combine(Root, "docs", "articles", "getting-started.md")).ReplaceLineEndings("\n");
        var documented = JsonNode.Parse(JsonBlock().Match(markdown).Groups["json"].Value)!.AsObject();

        var response = await factory.CreateClient().PostAsJsonAsync("/users", new { email = "", password = "short", age = "34" },
            TestContext.Current.CancellationToken);
        var actual = JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!.AsObject();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        documented.Remove("traceId");
        actual.Remove("traceId");
        Assert.True(JsonNode.DeepEquals(documented, actual), $"The article shows\n{documented}\nbut the endpoint returns\n{actual}");
    }

    // Whitespace-insensitive, so indentation and line breaks inside a sample may differ from the article.
    private static string Normalize(string code) => Whitespace().Replace(code, " ").Trim();

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ResultLabIUnion.slnx")))
                return directory.FullName;
        }

        throw new InvalidOperationException($"The repository root (ResultLabIUnion.slnx) was not found above {AppContext.BaseDirectory}.");
    }

    [GeneratedRegex(@"```csharp\n(?<code>.*?)```", RegexOptions.Singleline)]
    private static partial Regex CSharpBlock();

    [GeneratedRegex(@"```json\n(?<json>.*?)```", RegexOptions.Singleline)]
    private static partial Regex JsonBlock();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
