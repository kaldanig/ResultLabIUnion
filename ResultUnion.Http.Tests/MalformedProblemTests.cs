using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace ResultUnion.Http.Tests;

/// <summary>Problem bodies that are incomplete or not quite what ResultUnion sends: the reader keeps what it can use.</summary>
public sealed class MalformedProblemTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static HttpResponseMessage Response(HttpStatusCode status, string body, string mediaType = "application/problem+json") =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, mediaType) };

    [Fact]
    public async Task A_plain_JSON_body_is_read_too()
    {
        using var response = Response(HttpStatusCode.NotFound, """{"code":"user.not_found","detail":"No user."}""", "application/json");

        var error = await response.ReadErrorAsync(Ct);

        Assert.Equal("user.not_found", error.Code);
        Assert.Equal("No user.", error.Message);
    }

    [Fact]
    public async Task A_success_with_an_empty_body_is_a_bug()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([]) };

        await Assert.ThrowsAsync<InvalidOperationException>(() => response.ReadResultAsync<string>(Ct));
    }

    [Fact]
    public async Task The_category_must_match_an_ErrorType_name_exactly()
    {
        using var response = Response(HttpStatusCode.Conflict, """{"code":"order.changed","category":"concurrency"}""");

        Assert.Equal(ErrorType.Conflict, (await response.ReadErrorAsync(Ct)).Type);
    }

    [Fact]
    public async Task A_single_detail_without_a_field_leaves_the_error_without_one()
    {
        using var response = Response(HttpStatusCode.Conflict,
            """{"code":"user.email_taken","detail":"Taken.","details":[{"code":"user.email_taken","message":"Taken."}]}""");

        var error = await response.ReadErrorAsync(Ct);

        Assert.Equal("user.email_taken", error.Code);
        Assert.Null(error.Field);
    }

    [Fact]
    public async Task Detail_entries_without_code_or_message_or_of_the_wrong_kind_are_skipped()
    {
        using var response = Response(HttpStatusCode.BadRequest, """
            {"code":"validation.failed","detail":"Invalid.","details":[
              "text", {"code":"a.missing_message"}, {"message":"No code."},
              {"code":"email.required","message":"Required.","field":"email"},
              {"code":"form.invalid","message":"The form is invalid."}
            ]}
            """);

        var error = await response.ReadErrorAsync(Ct);

        Assert.Equal(
            [("email.required", "email"), ("form.invalid", (string?)null)],
            error.Details.Select(d => (d.Code, d.Field)));
    }

    [Fact]
    public async Task A_details_member_that_is_not_a_list_falls_back_to_errors()
    {
        using var response = Response(HttpStatusCode.BadRequest,
            """{"title":"Invalid.","details":"none","errors":{"email":["Required."],"age":["Too low."]}}""");

        var error = await response.ReadErrorAsync(Ct);

        Assert.Equal(["email", "age"], error.Details.Select(d => d.Field!));
    }

    [Fact]
    public async Task Errors_entries_that_are_not_message_lists_and_empty_messages_are_skipped()
    {
        using var response = Response(HttpStatusCode.BadRequest,
            """{"title":"Invalid.","errors":{"email":"Required.","name":["", "Too long."],"":["The form is invalid."]}}""");

        var error = await response.ReadErrorAsync(Ct);

        Assert.Equal(
            [("name", "Too long."), ((string?)null, "The form is invalid.")],
            error.Details.Select(d => (d.Field, d.Message)));
    }

    [Fact]
    public async Task An_errors_member_that_is_not_an_object_is_ignored()
    {
        using var response = Response(HttpStatusCode.BadRequest, """{"title":"Invalid.","errors":["Required."]}""");

        var error = await response.ReadErrorAsync(Ct);

        Assert.Equal("http.bad_request", error.Code);
        Assert.Empty(error.Details);
    }

    [Fact]
    public async Task False_and_unnamed_arguments_are_handled()
    {
        using var response = Response(HttpStatusCode.UnprocessableEntity, """{"code":"rule","arguments":{"flag":false," ":1}}""");

        var arguments = (await response.ReadErrorAsync(Ct)).Arguments;

        Assert.Equal(false, arguments["flag"]);
        Assert.Single(arguments);
    }

    [Fact]
    public async Task A_Retry_After_date_becomes_the_seconds_until_then()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddSeconds(90));
        using var past = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        past.Headers.RetryAfter = new RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddMinutes(-5));

        var seconds = Assert.IsType<int>((await response.ReadErrorAsync(Ct)).Arguments["retryAfterSeconds"]);
        Assert.InRange(seconds, 80, 91);
        Assert.Equal(0, (await past.ReadErrorAsync(Ct)).Arguments["retryAfterSeconds"]);
    }

    [Fact]
    public async Task Without_detail_title_or_reason_the_message_names_the_status()
    {
        using var response = Response(HttpStatusCode.NotFound, """{"code":"x.y"}""");
        response.ReasonPhrase = null;

        var error = await response.ReadErrorAsync(Ct);

        Assert.False(string.IsNullOrWhiteSpace(error.Message));
    }

    [Fact]
    public async Task A_group_of_another_type_keeps_the_top_level_error()
    {
        // ResultUnion groups only validation errors, so a conflict with several details cannot be rebuilt as a group.
        using var response = Response(HttpStatusCode.Conflict, """
            {"code":"order.conflict","detail":"Two problems.","category":"Conflict","details":[
              {"code":"order.shipped","message":"Shipped."},
              {"code":"order.paid","message":"Paid."}
            ]}
            """);

        var error = await response.ReadErrorAsync(Ct);

        Assert.Equal(("order.conflict", "Two problems.", ErrorType.Conflict), (error.Code, error.Message, error.Type));
        Assert.Empty(error.Details);
    }

    [Fact]
    public async Task A_response_reads_as_a_result_without_a_value()
    {
        using var success = new HttpResponseMessage(HttpStatusCode.NoContent);
        using var failure = Response(HttpStatusCode.NotFound, """{"code":"user.not_found","detail":"No user."}""");

        Assert.True((await success.ReadResultAsync(Ct)).IsSuccess);
        Assert.Equal("user.not_found", (await failure.ReadResultAsync(Ct)).Error.Code);
    }
}
