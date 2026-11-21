using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using ResultUnion.Diagnostics;

namespace ResultUnion.Tests.Diagnostics;

/// <summary>
/// Collects what ResultTelemetry reports, with the .NET listeners an OpenTelemetry SDK uses. Measurements are kept
/// per operation, because tests run in parallel and share the one meter.
/// </summary>
internal sealed class TelemetryCapture : IDisposable
{
    // A constant, not Source.Name: a listener of another test may run ShouldListenTo while Source is being created.
    private const string SourceName = "ResultUnion.Tests";

    public static readonly ActivitySource Source = new(SourceName);

    private readonly MeterListener _meters = new();
    private readonly ActivityListener _activities;
    private readonly ConcurrentQueue<Measurement> _measurements = new();

    public sealed record Measurement(long Value, IReadOnlyDictionary<string, object?> Tags);

    public TelemetryCapture()
    {
        _meters.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == ResultTelemetry.MeterName)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _meters.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            var copy = new Dictionary<string, object?>();
            foreach (var tag in tags)
            {
                copy[tag.Key] = tag.Value;
            }
            _measurements.Enqueue(new Measurement(value, copy));
        });
        _meters.Start();

        _activities = new ActivityListener
        {
            ShouldListenTo = source => source.Name == SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(_activities);
    }

    /// <summary>The measurements recorded for <paramref name="operation"/>.</summary>
    public IReadOnlyList<Measurement> For(string operation) =>
        [.. _measurements.Where(m => Equals(m.Tags.GetValueOrDefault(ResultTelemetry.OperationAttribute), operation))];

    public static Activity StartActivity() =>
        Source.StartActivity("test") ?? throw new InvalidOperationException("The test listener is not attached.");

    public void Dispose()
    {
        _meters.Dispose();
        _activities.Dispose();
    }
}
