using System.Diagnostics;
using ResultUnion.Diagnostics;

namespace ResultUnion.AspNetCore.Tests;

/// <summary>A problem response tags the request's span with the error, with no code in the endpoint.</summary>
public sealed class TelemetryTests
{
    private const string SourceName = "ResultUnion.AspNetCore.Tests";
    private static readonly ActivitySource Source = new(SourceName);

    [Fact]
    public void A_problem_response_tags_the_current_span()
    {
        using var listener = Listen();
        using var activity = Source.StartActivity("request")!;

        _ = Result<int>.Failure(Error.Conflict("Taken.").WithField("email")).ToHttpResult();

        Assert.Equal("conflict", activity.GetTagItem(ResultTelemetry.CodeAttribute));
        Assert.Equal("Conflict", activity.GetTagItem(ResultTelemetry.CategoryAttribute));
        Assert.Equal("email", activity.GetTagItem(ResultTelemetry.FieldAttribute));
        Assert.Equal(ActivityStatusCode.Unset, activity.Status);   // a 409 is not a server error
        Assert.Empty(activity.Events);                              // events come from an explicit Record only
    }

    [Fact]
    public async Task An_unexpected_error_marks_the_span_as_failed()
    {
        using var listener = Listen();
        using var activity = Source.StartActivity("request")!;

        _ = await Task.FromResult(Result.Failure(Error.Unexpected("Boom."))).ToHttpResultAsync();

        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Equal("unexpected", activity.GetTagItem(ResultTelemetry.CodeAttribute));
    }

    [Fact]
    public void A_success_leaves_the_span_alone()
    {
        using var listener = Listen();
        using var activity = Source.StartActivity("request")!;

        _ = Result<int>.Success(1).ToHttpResult();

        Assert.Empty(activity.Tags);
    }

    private static ActivityListener Listen()
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }
}
