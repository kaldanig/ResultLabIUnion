using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace ResultUnion.Diagnostics;

/// <summary>
/// Reports failures to OpenTelemetry through the .NET diagnostics APIs (<see cref="Meter"/>, <see cref="Activity"/>),
/// so the library needs no OpenTelemetry package. Enable it in the application's OpenTelemetry setup with
/// <c>.WithMetrics(metrics =&gt; metrics.AddMeter(ResultTelemetry.MeterName))</c>; traces need nothing extra, the
/// attributes go on spans that already exist.
/// </summary>
/// <remarks>
/// <para>What is recorded, for a failure only:</para>
/// <list type="bullet">
///   <item><description>
///     The counter <c>resultunion.errors</c> (unit <c>{error}</c>), with the attributes <c>error.type</c> (the error
///     code), <c>resultunion.error.category</c> (the <see cref="ErrorType"/> name) and <c>resultunion.operation</c>.
///   </description></item>
///   <item><description>
///     On the current span: the attributes <c>resultunion.error.code</c>, <c>resultunion.error.category</c> and, when
///     set, <c>resultunion.error.field</c>; with <see cref="Record"/> also an event <c>resultunion.error</c> carrying
///     the same values and the operation. The span status becomes <see cref="ActivityStatusCode.Error"/> only for
///     <see cref="ErrorType.Unexpected"/> and <see cref="ErrorType.Unavailable"/>, the categories that are server
///     failures (500 and 503); the others are expected outcomes, like a 4xx, and leave the status alone.
///   </description></item>
/// </list>
/// <para>
/// The message and the arguments are never recorded: they may contain personal data (an email, a value the user typed),
/// and the code already identifies the error. Codes come from catalogs, so the attribute values stay low-cardinality.
/// </para>
/// </remarks>
public static class ResultTelemetry
{
    /// <summary>The name of the <see cref="Meter"/>: <c>ResultUnion</c>.</summary>
    public const string MeterName = "ResultUnion";

    /// <summary>The name of the failure counter: <c>resultunion.errors</c>.</summary>
    public const string ErrorsCounterName = "resultunion.errors";

    /// <summary>The OpenTelemetry attribute for the error code on the counter: <c>error.type</c>.</summary>
    public const string ErrorTypeAttribute = "error.type";

    /// <summary>The attribute for the error code on a span: <c>resultunion.error.code</c>.</summary>
    public const string CodeAttribute = "resultunion.error.code";

    /// <summary>The attribute for the <see cref="ErrorType"/> name: <c>resultunion.error.category</c>.</summary>
    public const string CategoryAttribute = "resultunion.error.category";

    /// <summary>The attribute for the error's field, when it has one: <c>resultunion.error.field</c>.</summary>
    public const string FieldAttribute = "resultunion.error.field";

    /// <summary>The attribute for the operation that failed: <c>resultunion.operation</c>.</summary>
    public const string OperationAttribute = "resultunion.operation";

    /// <summary>The name of the span event added by <see cref="Record"/>: <c>resultunion.error</c>.</summary>
    public const string ErrorEventName = "resultunion.error";

    private static readonly Meter Meter = new(MeterName, typeof(ResultTelemetry).Assembly.GetName().Version?.ToString());

    private static readonly Counter<long> Errors = Meter.CreateCounter<long>(
        ErrorsCounterName,
        unit: "{error}",
        description: "Failed operations, by error code, category and operation.");

    /// <summary>
    /// Counts the failure of <paramref name="operation"/> and adds it to the current span (attributes and an event).
    /// </summary>
    /// <param name="error">The failure.</param>
    /// <param name="operation">A stable, low-cardinality name such as <c>users.register</c>: never an id or user input.</param>
    /// <exception cref="ArgumentNullException"><paramref name="error"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="operation"/> is <see langword="null"/>, empty or white space.</exception>
    public static void Record(Error error, string operation)
    {
        ArgumentNullException.ThrowIfNull(error);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);

        if (Errors.Enabled)
        {
            Errors.Add(
                1,
                new KeyValuePair<string, object?>(ErrorTypeAttribute, error.Code),
                new KeyValuePair<string, object?>(CategoryAttribute, error.Type.ToString()),
                new KeyValuePair<string, object?>(OperationAttribute, operation));
        }

        var activity = Activity.Current;
        if (activity is null || !activity.IsAllDataRequested)
        {
            return;
        }

        TagActivity(activity, error);

        var tags = new ActivityTagsCollection
        {
            [CodeAttribute] = error.Code,
            [CategoryAttribute] = error.Type.ToString(),
            [OperationAttribute] = operation,
        };
        if (error.Field is not null)
        {
            tags[FieldAttribute] = error.Field;
        }

        activity.AddEvent(new ActivityEvent(ErrorEventName, tags: tags));
    }

    /// <summary>
    /// Adds the error's code, category and field to <paramref name="activity"/>, and sets its status to
    /// <see cref="ActivityStatusCode.Error"/> for <see cref="ErrorType.Unexpected"/> and <see cref="ErrorType.Unavailable"/>.
    /// Does nothing for a <see langword="null"/> activity or one that is not recorded.
    /// </summary>
    /// <remarks>
    /// The HTTP and gRPC integrations call this for the current span when a failure leaves the service, so a server span
    /// shows which error a request ended with. Call it yourself where an error leaves by another way, such as a message
    /// consumer. A later call overwrites the attributes; the status is never set back from Error.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="error"/> is <see langword="null"/>.</exception>
    public static void TagActivity(Activity? activity, Error error)
    {
        ArgumentNullException.ThrowIfNull(error);

        if (activity is null || !activity.IsAllDataRequested)
        {
            return;
        }

        activity.SetTag(CodeAttribute, error.Code);
        activity.SetTag(CategoryAttribute, error.Type.ToString());
        if (error.Field is not null)
        {
            activity.SetTag(FieldAttribute, error.Field);
        }

        if (error.Type is ErrorType.Unexpected or ErrorType.Unavailable)
        {
            activity.SetStatus(ActivityStatusCode.Error, error.Code);
        }
    }
}
