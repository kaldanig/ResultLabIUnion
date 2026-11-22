// docs/articles/localization.md as tests: the server translates a problem response for the request's language, and a
// client translates an error itself from its code and arguments.
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using ResultUnion.Http;

namespace ResultUnion.Articles.Tests;

public sealed class LocalizationArticle(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly RegisterUser Invalid = new("", "short", "34");

    [Fact]
    public async Task The_server_answers_in_the_language_of_the_request()
    {
        var http = factory.CreateClient();
        http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("ka");

        var response = await http.PostAsJsonAsync("/users", Invalid, Ct);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("ერთი ან მეტი ველი არასწორია.", problem.GetProperty("detail").GetString());
        Assert.Equal("ელფოსტა სავალდებულოა.", problem.GetProperty("errors").GetProperty("email")[0].GetString());
        Assert.Equal("პაროლი უნდა იყოს მინიმუმ 8 სიმბოლო.", problem.GetProperty("errors").GetProperty("password")[0].GetString());
        Assert.Equal("password.too_short", problem.GetProperty("details")[1].GetProperty("code").GetString());  // codes stay as they are
    }

    [Fact]
    public async Task The_Georgian_problem_response_in_the_article_is_the_real_one()
    {
        var markdown = File.ReadAllText(Path.Combine(ArticleCodeTests.Root, "docs", "articles", "localization.md")).ReplaceLineEndings("\n");
        var documented = JsonNode.Parse(Regex.Match(markdown, @"```json\n(?<json>.*?)```", RegexOptions.Singleline).Groups["json"].Value)!.AsObject();
        var http = factory.CreateClient();
        http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("ka");

        var response = await http.PostAsJsonAsync("/users", Invalid, Ct);
        var actual = JsonNode.Parse(await response.Content.ReadAsStringAsync(Ct))!.AsObject();

        documented.Remove("traceId");
        actual.Remove("traceId");
        Assert.True(JsonNode.DeepEquals(documented, actual), $"The article shows\n{documented}\nbut the endpoint returns\n{actual}");
    }

    [Fact]
    public async Task Without_a_translation_the_message_is_the_one_the_error_was_created_with()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/users", Invalid, Ct);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);

        Assert.Equal("Password must be at least 8 characters.", problem.GetProperty("errors").GetProperty("password")[0].GetString());
    }

    [Fact]
    public async Task A_client_translates_the_errors_it_receives()
    {
        var http = factory.CreateClient();  // no Accept-Language: the server answers in English
        var messages = factory.Services.GetRequiredService<ErrorMessages>();
        var shown = new Dictionary<string, string>();
        void Show(string? field, string message) => shown[field ?? ""] = message;
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = new CultureInfo("ka");
        try
        {
            var result = await http.PostAsJsonAsync("/users", Invalid, Ct).ReadResultAsync<UserResponse>(Ct);
            if (result.TryGetError(out var error))
            {
                foreach (var detail in error.Details)
                    Show(detail.Field, messages.Describe(detail));
            }
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }

        Assert.Equal("ელფოსტა სავალდებულოა.", shown["email"]);
        Assert.Equal("პაროლი უნდა იყოს მინიმუმ 8 სიმბოლო.", shown["password"]);
    }
}
