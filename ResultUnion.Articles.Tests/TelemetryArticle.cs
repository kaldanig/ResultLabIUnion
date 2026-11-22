// docs/articles/telemetry.md as tests: the counter and the span attributes the article lists, read with the .NET
// listeners that OpenTelemetry itself builds on.
using System.Diagnostics;
using System.Diagnostics.Metrics;
using ResultUnion.Diagnostics;

namespace ResultUnion.Articles.Tests;

public sealed class TelemetryArticle
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly RegisterUser Adult = new("ann@example.com", "s3cret!pass", "34");
    private static readonly RegisterUser Minor = new("kid@example.com", "s3cret!pass", "15");

    [Fact]
    public async Task A_failure_is_counted_with_its_code_category_and_operation_and_a_success_is_not()
    {
        var service = new RecordedUserService(new InMemoryUsers());

        var counted = await CountAsync(async () =>
        {
            _ = await service.RegisterAsync(Adult, Ct);
            _ = await service.RegisterAsync(Minor, Ct);
        });

        var measurement = Assert.Single(counted);
        Assert.Equal("user.too_young", measurement["error.type"]);
        Assert.Equal("BusinessRule", measurement["resultunion.error.category"]);
        Assert.Equal("users.register", measurement["resultunion.operation"]);
    }

    [Fact]
    public async Task RecordAsync_puts_the_error_on_the_current_span_with_an_event()
    {
        var service = new RecordedUserService(new InMemoryUsers());
        using var source = new ActivitySource("TelemetryArticle.Record");
        using var listener = Listen(source.Name);

        using var activity = source.StartActivity("request")!;
        _ = await service.RegisterAsync(Minor, Ct);   // only what it reports matters here

        Assert.Equal("user.too_young", activity.GetTagItem(ResultTelemetry.CodeAttribute));
        Assert.Equal("BusinessRule", activity.GetTagItem(ResultTelemetry.CategoryAttribute));
        var recorded = Assert.Single(activity.Events, e => e.Name == ResultTelemetry.ErrorEventName);
        Assert.Contains(recorded.Tags, tag => tag is { Key: ResultTelemetry.OperationAttribute, Value: "users.register" });
        Assert.Equal(ActivityStatusCode.Unset, activity.Status);     // an expected outcome, like a 4xx
    }

    [Fact]
    public async Task A_consumer_tags_its_own_span()
    {
        var consumer = new RegistrationConsumer(new UserService(new InMemoryUsers()));
        Activity? consumed = null;
        using var listener = Listen(RegistrationConsumer.Source.Name, stopped: activity => consumed = activity);

        await consumer.ConsumeAsync(new RegisterUser("", "s3cret!pass", "34"), Ct);

        Assert.NotNull(consumed);
        Assert.Equal("email.required", consumed.GetTagItem(ResultTelemetry.CodeAttribute));
        Assert.Equal("email", consumed.GetTagItem(ResultTelemetry.FieldAttribute));
    }

    // The counter's measurements of the operation in this file only: other tests may record at the same time.
    private static async Task<List<Dictionary<string, object?>>> CountAsync(Func<Task> act)
    {
        var counted = new List<Dictionary<string, object?>>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == ResultTelemetry.MeterName && instrument.Name == ResultTelemetry.ErrorsCounterName)
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            var values = new Dictionary<string, object?>();
            foreach (var tag in tags)
                values[tag.Key] = tag.Value;

            if (values.GetValueOrDefault(ResultTelemetry.OperationAttribute) is "users.register")
            {
                lock (counted)
                    counted.Add(values);
            }
        });
        listener.Start();

        await act();
        return counted;
    }

    private static ActivityListener Listen(string sourceName, Action<Activity>? stopped = null)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == sourceName,
            Sample = (ref _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = stopped,
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }
}
