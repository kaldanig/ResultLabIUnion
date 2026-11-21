using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ResultUnion.WebApi.Tests;

/// <summary>
/// The Web API demo running in memory. Each test class gets one instance (xUnit class fixture), so tests use their own
/// email addresses; ann@example.com is seeded at startup. OpenTelemetry is switched off, so no test tries to reach an
/// OTLP collector.
/// </summary>
public sealed class WebApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.UseSetting("OTEL_SDK_DISABLED", "true");
}

internal static class Http
{
    public static string NewEmail() => $"user-{Guid.NewGuid():N}@example.com";

    public static async Task<(HttpResponseMessage Response, JsonElement Body)> SendAsync(
        this HttpClient client, HttpMethod method, string url, object? body = null, string? language = null, string? role = null)
    {
        using var request = new HttpRequestMessage(method, url);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        if (language is not null)
            request.Headers.AcceptLanguage.ParseAdd(language);
        if (role is not null)
            request.Headers.Add("X-Role", role);

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var json = text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone();
        return (response, json);
    }

    public static async Task<HttpResponseMessage> RegisterAsync(this HttpClient client, string email, string age = "30")
    {
        var (response, _) = await client.SendAsync(HttpMethod.Post, "/users", new { email, password = "secret123", age });
        Assert.Equal(System.Net.HttpStatusCode.Created, response.StatusCode);
        return response;
    }

    /// <summary>The <c>errors</c> object of a validation problem as field → messages.</summary>
    public static Dictionary<string, string[]> Errors(this JsonElement problem) =>
        problem.GetProperty("errors").EnumerateObject()
            .ToDictionary(field => field.Name, field => field.Value.EnumerateArray().Select(m => m.GetString()!).ToArray());

    /// <summary>The <c>details</c> array of a validation problem as field → code.</summary>
    public static Dictionary<string, string> DetailCodes(this JsonElement problem) =>
        problem.GetProperty("details").EnumerateArray()
            .ToDictionary(detail => detail.GetProperty("field").GetString()!, detail => detail.GetProperty("code").GetString()!);
}
