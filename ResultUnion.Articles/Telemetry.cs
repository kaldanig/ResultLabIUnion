using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using ResultUnion.Diagnostics;

namespace ResultUnion.Articles;

// docs/articles/telemetry.md. ResultUnion.Articles.Tests (TelemetryArticle) listens to what these report.

/// <summary>The registration of getting-started.md, ending with RecordAsync.</summary>
public sealed class RecordedUserService(IUserRepository users)
{
    public Task<Result<User>> RegisterAsync(RegisterUser command, CancellationToken ct) =>
        UserService.Validate(command)
            .Ensure(user => user.Age >= 18, User.Errors.TooYoung)
            .EnsureAsync(user => users.EnsureEmailIsFreeAsync(user.Email, ct))
            .TapAsync(user => users.AddAsync(user, ct))
            .RecordAsync("users.register");                     // a failure is counted by its code
}

public static class TelemetrySetup
{
    /// <summary>The application's OpenTelemetry setup; the one line for ResultUnion is AddMeter.</summary>
    public static void AddTelemetry(WebApplicationBuilder builder) =>
        builder.Services.AddOpenTelemetry()
            .WithTracing(tracing => tracing.AddAspNetCoreInstrumentation())
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddMeter(ResultTelemetry.MeterName))           // resultunion.errors
            .UseOtlpExporter();
}

/// <summary>A message consumer: the error leaves through no HTTP or gRPC host that would tag the span.</summary>
public sealed class RegistrationConsumer(UserService service)
{
    public static readonly ActivitySource Source = new("ResultUnion.Articles.Registrations");

    public async Task ConsumeAsync(RegisterUser message, CancellationToken ct)
    {
        using var activity = Source.StartActivity("registrations.consume", ActivityKind.Consumer);

        var result = await service.RegisterAsync(message, ct);
        if (result.TryGetError(out var error))
            ResultTelemetry.TagActivity(activity, error);       // the code and category on the consumer's span
    }
}
