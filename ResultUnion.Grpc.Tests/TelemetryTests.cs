using System.Diagnostics;
using Grpc.Core;
using ResultUnion.Diagnostics;

namespace ResultUnion.Grpc.Tests;

/// <summary>A failure that leaves a gRPC service tags the call's span with the error.</summary>
public sealed class TelemetryTests
{
    private const string SourceName = "ResultUnion.Grpc.Tests";
    private static readonly ActivitySource Source = new(SourceName);

    [Fact]
    public void GetValueOrThrowRpc_tags_the_current_span()
    {
        using var listener = Listen();
        using var activity = Source.StartActivity("call")!;

        Assert.Throws<RpcException>(() => Result<int>.Failure(Error.NotFound("No user.")).GetValueOrThrowRpc());

        Assert.Equal("resource.not_found", activity.GetTagItem(ResultTelemetry.CodeAttribute));
        Assert.Equal("NotFound", activity.GetTagItem(ResultTelemetry.CategoryAttribute));
        Assert.Equal(ActivityStatusCode.Unset, activity.Status);
    }

    [Fact]
    public async Task An_unavailable_dependency_marks_the_span_as_failed()
    {
        using var listener = Listen();
        using var activity = Source.StartActivity("call")!;

        await Assert.ThrowsAsync<RpcException>(() =>
            Task.FromResult(Result.Failure(Error.Unavailable("Database down."))).GetValueOrThrowRpcAsync(() => 0));

        Assert.Equal(ActivityStatusCode.Error, activity.Status);
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
