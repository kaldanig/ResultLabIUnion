# Seeing failures in telemetry

Every expected failure in a ResultUnion application has a stable code, so the questions that matter in production
have exact answers: which errors happen, how often, in which operation, and in which request. This page shows how to
record them, what reaches OpenTelemetry, and how to read it in a dashboard. The code is in `ResultUnion.Articles`
(`Telemetry.cs`), and `ResultUnion.Articles.Tests` (`TelemetryArticle.cs`) checks every name below.

## Record where the operation ends

A use case ends its pipeline with `RecordAsync` (or `Record` for a result that is not a task):

```csharp
    public Task<Result<User>> RegisterAsync(RegisterUser command, CancellationToken ct) =>
        UserService.Validate(command)
            .Ensure(user => user.Age >= 18, User.Errors.TooYoung)
            .EnsureAsync(user => users.EnsureEmailIsFreeAsync(user.Email, ct))
            .TapAsync(user => users.AddAsync(user, ct))
            .RecordAsync("users.register");                     // a failure is counted by its code
```

It returns the result unchanged and does nothing for a success. For a failure it adds one to a counter and puts the
error on the current span. The operation name becomes an attribute of the counter, so it is a fixed, low-cardinality
name (`users.register`), never an id or anything a user typed. Record at the end of the use case, not at every step:
a failure that a later step handles is not a failure of the operation.

`RecordAsync` is in the namespace `ResultUnion`, like `Bind` and `Map`, so a handler needs no other `using`.

## Turn it on

The library uses `Meter` and `Activity`, which are part of .NET, so it references no OpenTelemetry package. The
application adds the meter in its own setup, and that is the only line about ResultUnion:

```csharp
    public static void AddTelemetry(WebApplicationBuilder builder) =>
        builder.Services.AddOpenTelemetry()
            .WithTracing(tracing => tracing.AddAspNetCoreInstrumentation())
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddMeter(ResultTelemetry.MeterName))           // resultunion.errors
            .UseOtlpExporter();
```

Traces need nothing more: the attributes go on spans that already exist, such as the request span from the ASP.NET
Core instrumentation. When nothing listens, recording costs a check and does nothing.

## What is recorded

The counter `resultunion.errors` (meter `ResultUnion`, unit `{error}`), one per failure:

| Attribute | Value |
|---|---|
| `error.type` | the error code, `user.too_young` |
| `resultunion.error.category` | the `ErrorType`, `BusinessRule` |
| `resultunion.operation` | the name given to `RecordAsync`, `users.register` |

On the current span:

| Attribute | Value |
|---|---|
| `resultunion.error.code` | the error code |
| `resultunion.error.category` | the `ErrorType` |
| `resultunion.error.field` | the field, when the error has one |

`Record` and `RecordAsync` also add an event `resultunion.error` with the same values and the operation. The span's
status becomes Error only for `Unexpected` and `Unavailable`, the failures that are the server's (500 and 503). The
others are expected outcomes, as a 4xx is in the OpenTelemetry HTTP conventions, and leave the status alone, so an
error rate built on span status still means "something broke".

The message and the arguments are never recorded. They can hold personal data (an email, a value the user typed), and
the code already says which error it was. Codes come from catalogs, so the attribute values stay a small, fixed set.

## Hosts tag the request span by themselves

`ToHttpResult` and `GetValueOrThrowRpc` put the code, the category and the field on the server span when a failure
leaves the service, with or without `RecordAsync`. A span's attributes can be set twice without harm, so this never
counts anything double; the counter, which cannot be deduplicated, stays with `RecordAsync`.

Where an error leaves some other way, such as a message consumer, tag the span yourself:

```csharp
        using var activity = Source.StartActivity("registrations.consume", ActivityKind.Consumer);

        var result = await service.RegisterAsync(message, ct);
        if (result.TryGetError(out var error))
            ResultTelemetry.TagActivity(activity, error);       // the code and category on the consumer's span
```

## Reading it in a dashboard

**Which errors, how often.** Group `resultunion.errors` by `error.type`. A new code at the top after a release is
usually the first sign of a change in behavior; filter by `resultunion.operation` to see one use case. In Prometheus,
where the exporter turns the name into `resultunion_errors_total` and the attributes into labels:

```text
sum by (error_type) (rate(resultunion_errors_total[5m]))
```

**What to alert on.** Not on the total: validation failures go up and down with traffic and with what clients send.
Alert on the categories that need someone, `Unexpected` and `Unavailable`, and on a code whose rate is far from its
usual one:

```text
sum(rate(resultunion_errors_total{resultunion_error_category=~"Unexpected|Unavailable"}[5m])) > 0
```

**Which request.** Search spans for `resultunion.error.code = user.too_young`. The span shows the request that ended
with that error, and the trace shows the rest of the request: its database calls, and the call that came before it.

**Across services.** When one service calls another, the trace shows both. A gRPC client that reads a failed call back
with `ToError()` and ends with `RecordAsync` puts the same code on its own span, so one trace shows the client's
operation, the call, and the service's span with `grpc.status_code` and `resultunion.error.code`. The repository's
[`ResultUnion.AppHost`](https://github.com/kaldanig/ResultLabIUnion/tree/master/ResultUnion.AppHost) runs the demos
this way under Aspire, with the traces and the counter in Aspire's dashboard:

```powershell
dotnet run --project ResultUnion.AppHost --launch-profile http
```

## Testing it

The names are part of the 1.x promise, but what your use cases record is yours to check. `MeterListener` and
`ActivityListener`, which OpenTelemetry builds on, read the measurements in a test without an exporter:

```csharp
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == ResultTelemetry.MeterName && instrument.Name == ResultTelemetry.ErrorsCounterName)
                l.EnableMeasurementEvents(instrument);
        };
```

`TelemetryArticle` checks that a failed registration is counted once with its code, category and operation, that a
success is not, and that the span gets the attributes and the event.
