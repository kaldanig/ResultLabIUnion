using System.Diagnostics;
using System.Diagnostics.Metrics;
using ResultUnion.Diagnostics;

namespace ResultUnion.Showcase.Demos;

/// <summary>
/// OpenTelemetry: what <c>Record</c> reports. An application enables it with
/// <c>.WithMetrics(m =&gt; m.AddMeter(ResultTelemetry.MeterName))</c>; here the same .NET listeners an OpenTelemetry
/// SDK uses print the counter and the span.
/// </summary>
internal static class TelemetryDemo
{
    private static readonly ActivitySource Source = new("ResultUnion.Showcase");

    public static void Run()
    {
        using var meters = new MeterListener();
        meters.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == ResultTelemetry.MeterName)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        meters.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            var attributes = string.Join(", ", tags.ToArray().Select(tag => $"{tag.Key}={tag.Value}"));
            Console.WriteLine($"  metric  {instrument.Name} +{value}  {{{attributes}}}");
        });
        meters.Start();

        using var spans = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "ResultUnion.Showcase",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(spans);

        var service = new RegistrationService(new UserRepository());

        using (var activity = Source.StartActivity("register")!)
        {
            _ = service.Register("bob@example.com", "15").Record("users.register");          // a failure: counted
            _ = service.Register("ann@example.com", "34").Record("users.register");          // a success: nothing

            var attributes = string.Join(", ", activity.Tags.Select(tag => $"{tag.Key}={tag.Value}"));
            Console.WriteLine($"  span    {activity.DisplayName}: status {activity.Status}  {{{attributes}}}");
            foreach (var recorded in activity.Events)
            {
                Console.WriteLine($"  event   {recorded.Name}  {{{string.Join(", ", recorded.Tags.Select(tag => $"{tag.Key}={tag.Value}"))}}}");
            }
        }

        using (var activity = Source.StartActivity("load")!)
        {
            _ = Result<int>.Failure(Error.Unavailable("The database is down.")).Record("orders.load");
            Console.WriteLine($"  span    {activity.DisplayName}: status {activity.Status} ({activity.StatusDescription})");
        }

        Console.WriteLine("  Message and arguments are never recorded: they may contain personal data.");
    }
}
