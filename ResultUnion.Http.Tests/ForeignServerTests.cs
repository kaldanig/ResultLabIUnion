using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace ResultUnion.Http.Tests;

/// <summary>Responses from servers that know nothing about ResultUnion: status codes, plain problem details, HTML pages.</summary>
public sealed class ForeignServerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static HttpResponseMessage Response(HttpStatusCode status, string? body = null, string mediaType = "application/problem+json")
    {
        var response = new HttpResponseMessage(status);
        if (body is not null)
            response.Content = new StringContent(body, Encoding.UTF8, mediaType);
        return response;
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, ErrorType.Validation, "http.bad_request")]
    [InlineData(HttpStatusCode.Unauthorized, ErrorType.Unauthorized, "http.unauthorized")]
    [InlineData(HttpStatusCode.Forbidden, ErrorType.Forbidden, "http.forbidden")]
    [InlineData(HttpStatusCode.NotFound, ErrorType.NotFound, "http.not_found")]
    [InlineData(HttpStatusCode.Conflict, ErrorType.Conflict, "http.conflict")]
    [InlineData(HttpStatusCode.UnprocessableEntity, ErrorType.BusinessRule, "http.unprocessable_entity")]
    [InlineData(HttpStatusCode.TooManyRequests, ErrorType.TooManyRequests, "http.too_many_requests")]
    [InlineData(HttpStatusCode.RequestTimeout, ErrorType.Unavailable, "http.request_timeout")]
    [InlineData(HttpStatusCode.BadGateway, ErrorType.Unavailable, "http.bad_gateway")]
    [InlineData(HttpStatusCode.ServiceUnavailable, ErrorType.Unavailable, "http.service_unavailable")]
    [InlineData(HttpStatusCode.GatewayTimeout, ErrorType.Unavailable, "http.gateway_timeout")]
    [InlineData(HttpStatusCode.InternalServerError, ErrorType.Unexpected, "http.internal_server_error")]
    [InlineData(HttpStatusCode.MethodNotAllowed, ErrorType.Unexpected, "http.method_not_allowed")]
    [InlineData((HttpStatusCode)599, ErrorType.Unexpected, "http.599")]
    public async Task Without_a_body_the_status_decides_type_and_code(HttpStatusCode status, ErrorType type, string code)
    {
        using var response = Response(status);
        response.ReasonPhrase = "Reason";

        var error = await response.ReadErrorAsync(Ct);

        Assert.Equal(type, error.Type);
        Assert.Equal(code, error.Code);
        Assert.Equal("Reason", error.Message);
    }

    [Fact]
    public async Task A_plain_problem_uses_detail_then_title()
    {
        using var withDetail = Response(HttpStatusCode.NotFound, """{"title":"Not Found","detail":"No order 7.","status":404}""");
        using var withTitle = Response(HttpStatusCode.NotFound, """{"title":"Not Found","status":404}""");

        Assert.Equal("No order 7.", (await withDetail.ReadErrorAsync(Ct)).Message);
        Assert.Equal("Not Found", (await withTitle.ReadErrorAsync(Ct)).Message);
    }

    [Fact]
    public async Task ASP_NET_Core_validation_errors_become_field_errors()
    {
        using var response = Response(HttpStatusCode.BadRequest,
            """{"title":"One or more validation errors occurred.","status":400,"errors":{"Email":["The Email field is required."],"Age":["Too low.","Not a number."]}}""");

        var error = await response.ReadErrorAsync(Ct);

        Assert.Equal("validation.failed", error.Code);
        Assert.Equal(
            [("Email", "The Email field is required."), ("Age", "Too low."), ("Age", "Not a number.")],
            error.Details.Select(d => (d.Field!, d.Message)));
    }

    [Fact]
    public async Task One_validation_error_comes_back_as_that_field_error()
    {
        using var response = Response(HttpStatusCode.BadRequest, """{"title":"Invalid","status":400,"errors":{"email":["Required."]}}""");

        var error = await response.ReadErrorAsync(Ct);

        Assert.Equal("validation.failed", error.Code);
        Assert.Equal("Required.", error.Message);
        Assert.Equal("email", error.Field);
        Assert.Empty(error.Details);
    }

    [Fact]
    public async Task Retry_After_becomes_an_argument_unless_the_body_has_one()
    {
        using var header = Response(HttpStatusCode.ServiceUnavailable);
        header.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(30));
        using var both = Response(HttpStatusCode.TooManyRequests, """{"code":"quota","arguments":{"retryAfterSeconds":5}}""");
        both.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(30));

        Assert.Equal(30, (await header.ReadErrorAsync(Ct)).Arguments["retryAfterSeconds"]);
        Assert.Equal(5, (await both.ReadErrorAsync(Ct)).Arguments["retryAfterSeconds"]);
    }

    [Fact]
    public async Task An_HTML_page_or_broken_JSON_falls_back_to_the_status()
    {
        using var html = Response(HttpStatusCode.BadGateway, "<html>Bad gateway</html>", "text/html");
        using var broken = Response(HttpStatusCode.InternalServerError, "{ not json", "application/json");
        using var array = Response(HttpStatusCode.InternalServerError, "[1,2]", "application/json");

        Assert.Equal("http.bad_gateway", (await html.ReadErrorAsync(Ct)).Code);
        Assert.Equal("http.internal_server_error", (await broken.ReadErrorAsync(Ct)).Code);
        Assert.Equal("http.internal_server_error", (await array.ReadErrorAsync(Ct)).Code);
    }

    [Fact]
    public async Task An_unknown_category_is_ignored()
    {
        using var response = Response(HttpStatusCode.Conflict, """{"code":"x.y","category":"Nonsense"}""");

        Assert.Equal(ErrorType.Conflict, (await response.ReadErrorAsync(Ct)).Type);
    }

    [Fact]
    public async Task Argument_values_are_read_like_ErrorJsonConverter_reads_them()
    {
        using var response = Response(HttpStatusCode.UnprocessableEntity,
            """{"code":"rule","arguments":{"int":1,"long":5000000000,"double":1.5,"text":"a","flag":true,"none":null,"list":[1]}}""");

        var arguments = (await response.ReadErrorAsync(Ct)).Arguments;

        Assert.Equal(1, arguments["int"]);
        Assert.Equal(5000000000L, arguments["long"]);
        Assert.Equal(1.5, arguments["double"]);
        Assert.Equal("a", arguments["text"]);
        Assert.Equal(true, arguments["flag"]);
        Assert.Null(arguments["none"]);
        Assert.Equal("[1]", Assert.IsType<System.Text.Json.JsonElement>(arguments["list"]).GetRawText());
    }

    // ---- contract --------------------------------------------------------------------------------------------------

    [Fact]
    public async Task ReadErrorAsync_rejects_a_successful_response()
    {
        using var response = Response(HttpStatusCode.OK, "{}", "application/json");

        await Assert.ThrowsAsync<InvalidOperationException>(() => response.ReadErrorAsync(Ct));
    }

    [Fact]
    public async Task A_success_without_a_value_is_a_bug_not_a_failure()
    {
        using var noContent = Response(HttpStatusCode.NoContent);
        using var nullBody = Response(HttpStatusCode.OK, "null", "application/json");
        using var badJson = Response(HttpStatusCode.OK, "{ not json", "application/json");

        await Assert.ThrowsAsync<InvalidOperationException>(() => noContent.ReadResultAsync<string>(Ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => nullBody.ReadResultAsync<string>(Ct));
        await Assert.ThrowsAnyAsync<System.Text.Json.JsonException>(() => badJson.ReadResultAsync<string>(Ct));
    }

    [Fact]
    public async Task The_task_forms_dispose_the_response_and_the_others_do_not()
    {
        var disposed = new TrackingContent("\"ann\"");
        var kept = new TrackingContent("\"ann\"");
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = kept };

        _ = await Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = disposed }).ReadResultAsync<string>(Ct);
        _ = await response.ReadResultAsync<string>(Ct);

        Assert.True(disposed.IsDisposed);
        Assert.False(kept.IsDisposed);
        response.Dispose();
    }

    [Fact]
    public async Task Cancellation_is_not_turned_into_a_failure()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        using var response = Response(HttpStatusCode.NotFound, """{"code":"x"}""");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => response.ReadResultAsync<string>(cancelled.Token));
    }

    private sealed class TrackingContent(string json) : StringContent(json, Encoding.UTF8, "application/json")
    {
        public bool IsDisposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }
}
