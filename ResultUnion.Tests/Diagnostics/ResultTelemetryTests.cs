using System.Diagnostics;
using ResultUnion.Diagnostics;

namespace ResultUnion.Tests.Diagnostics;

public sealed class ResultTelemetryTests
{
    private static readonly Error TooYoung =
        Error.Create("user.too_young", "Ann is 15.", ErrorType.BusinessRule).WithField("age").WithArgument("age", 15);

    // ---- metrics -----------------------------------------------------------------------------------------------------

    [Fact]
    public void A_failure_is_counted_with_code_category_and_operation()
    {
        using var capture = new TelemetryCapture();

        _ = Result<int>.Failure(TooYoung).Record("tests.count_failure");

        var measurement = Assert.Single(capture.For("tests.count_failure"));
        Assert.Equal(1, measurement.Value);
        Assert.Equal("user.too_young", measurement.Tags[ResultTelemetry.ErrorTypeAttribute]);
        Assert.Equal("BusinessRule", measurement.Tags[ResultTelemetry.CategoryAttribute]);
        Assert.Equal(3, measurement.Tags.Count);   // no message, field or arguments: low cardinality, no personal data
    }

    [Fact]
    public void A_success_records_nothing()
    {
        using var capture = new TelemetryCapture();
        using var activity = TelemetryCapture.StartActivity();

        _ = Result<int>.Success(1).Record("tests.success");
        _ = Result.Success().Record("tests.success");

        Assert.Empty(capture.For("tests.success"));
        Assert.Empty(activity.Tags);
        Assert.Empty(activity.Events);
    }

    [Fact]
    public async Task Every_result_type_records()
    {
        using var capture = new TelemetryCapture();

        _ = Result.Failure(TooYoung).Record("tests.every_type");
        _ = Result<int>.Failure(TooYoung).Record("tests.every_type");
        _ = await Task.FromResult(Result.Failure(TooYoung)).RecordAsync("tests.every_type");
        _ = await Task.FromResult(Result<int>.Failure(TooYoung)).RecordAsync("tests.every_type");
        _ = await Pending(Result.Failure(TooYoung)).RecordAsync("tests.every_type");
        _ = await Pending(Result<int>.Failure(TooYoung)).RecordAsync("tests.every_type");

        Assert.Equal(6, capture.For("tests.every_type").Count);
    }

    [Fact]
    public void The_result_comes_back_unchanged()
    {
        var failure = Result<int>.Failure(TooYoung);
        var completed = Task.FromResult(failure);

        Assert.Equal(failure, failure.Record("tests.unchanged"));
        Assert.Same(completed, completed.RecordAsync("tests.unchanged"));
    }

    [Fact]
    public void Arguments_are_validated_when_called()
    {
        Assert.Throws<ArgumentException>(() => Result.Success().Record(" "));
        // Statement lambdas (Action), so the exception must come from the call itself, not from the task.
        Assert.Throws<ArgumentNullException>(() => { _ = ((Task<Result<int>>)null!).RecordAsync("x"); });
        Assert.Throws<ArgumentException>(() => { _ = Task.FromResult(Result.Success()).RecordAsync(""); });
        Assert.Throws<ArgumentNullException>(() => ResultTelemetry.Record(null!, "x"));
    }

    // ---- traces ------------------------------------------------------------------------------------------------------

    [Fact]
    public void Record_tags_the_current_span_and_adds_an_event()
    {
        using var capture = new TelemetryCapture();
        using var activity = TelemetryCapture.StartActivity();

        _ = Result<int>.Failure(TooYoung).Record("users.register");

        Assert.Equal("user.too_young", activity.GetTagItem(ResultTelemetry.CodeAttribute));
        Assert.Equal("BusinessRule", activity.GetTagItem(ResultTelemetry.CategoryAttribute));
        Assert.Equal("age", activity.GetTagItem(ResultTelemetry.FieldAttribute));

        var recorded = Assert.Single(activity.Events);
        Assert.Equal(ResultTelemetry.ErrorEventName, recorded.Name);
        var tags = recorded.Tags.ToDictionary(tag => tag.Key, tag => tag.Value);
        Assert.Equal("users.register", tags[ResultTelemetry.OperationAttribute]);
        Assert.Equal("user.too_young", tags[ResultTelemetry.CodeAttribute]);
        Assert.DoesNotContain(tags.Values, value => value is string text && text.Contains("Ann"));   // no message
    }

    [Theory]
    [InlineData(ErrorType.Unexpected, ActivityStatusCode.Error)]
    [InlineData(ErrorType.Unavailable, ActivityStatusCode.Error)]
    [InlineData(ErrorType.Validation, ActivityStatusCode.Unset)]
    [InlineData(ErrorType.NotFound, ActivityStatusCode.Unset)]
    [InlineData(ErrorType.BusinessRule, ActivityStatusCode.Unset)]
    [InlineData(ErrorType.TooManyRequests, ActivityStatusCode.Unset)]
    public void Only_server_failures_set_the_span_status_to_error(ErrorType type, ActivityStatusCode expected)
    {
        using var capture = new TelemetryCapture();
        using var activity = TelemetryCapture.StartActivity();

        ResultTelemetry.TagActivity(activity, Error.Create("some.code", "Message.", type));

        Assert.Equal(expected, activity.Status);
        if (expected == ActivityStatusCode.Error)
        {
            Assert.Equal("some.code", activity.StatusDescription);
        }
    }

    [Fact]
    public void An_error_status_is_never_set_back()
    {
        using var capture = new TelemetryCapture();
        using var activity = TelemetryCapture.StartActivity();

        ResultTelemetry.TagActivity(activity, Error.Unexpected("Boom."));
        ResultTelemetry.TagActivity(activity, Error.Validation("Bad."));

        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Equal("validation.failed", activity.GetTagItem(ResultTelemetry.CodeAttribute));   // the last error wins
    }

    [Fact]
    public void Without_a_span_or_a_listener_nothing_happens()
    {
        Assert.Null(Activity.Current);

        ResultTelemetry.TagActivity(null, TooYoung);
        _ = Result<int>.Failure(TooYoung).Record("tests.no_listener");   // no meter listener: does not throw
    }

    private static async Task<T> Pending<T>(T result)
    {
        await Task.Yield();
        return result;
    }
}
