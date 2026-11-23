using Consul;
using Microsoft.Extensions.Options;
using ResultUnion.Consul.Rates.Rates;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace ResultUnion.Consul.Rates.Registration;

/// <summary>
/// Registers this instance in Consul once the server listens, then reports its TTL check at every heartbeat: passing
/// normally, critical during an outage. Consul lists only instances whose check passes, so an instance that stops
/// reporting (killed, hung) drops out of the converter's choice after the TTL, and one that stops cleanly deregisters.
/// A failed call to Consul never stops the service: it answers rate requests either way and tries again at the next beat.
/// </summary>
public sealed partial class ConsulRegistration(
    ConsulAgent agent,
    RatesInstance instance,
    Outage outage,
    IHostApplicationLifetime lifetime,
    IOptions<ConsulOptions> options,
    ILogger<ConsulRegistration> logger) : BackgroundService
{
    private volatile bool _registered;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await StartedAsync(stoppingToken).ConfigureAwait(false);

        using var timer = new PeriodicTimer(options.Value.Heartbeat);
        do
        {
            if (!_registered)
                _registered = await RegisterAsync(stoppingToken).ConfigureAwait(false);

            if (_registered)
                _registered = await HeartbeatAsync(stoppingToken).ConfigureAwait(false);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
        if (!_registered)
            return;

        // Once only: a host may stop its services again while it is disposed, after the Consul client is gone.
        _registered = false;
        var deregistered = await agent.DeregisterAsync(instance.Id, cancellationToken).ConfigureAwait(false);
        if (deregistered.TryGetError(out var error))
            LogDeregistrationFailed(instance.Id, error.Code);
        else
            LogDeregistered(instance.Id);
    }

    private async Task<bool> RegisterAsync(CancellationToken ct)
    {
        var registration = new AgentServiceRegistration
        {
            ID = instance.Id,
            Name = options.Value.ServiceName,
            Address = instance.Address.Host,
            Port = instance.Address.Port,
            Tags = ["resultunion-sample"],
            Check = new AgentServiceCheck
            {
                TTL = options.Value.CheckTtl,
                DeregisterCriticalServiceAfter = TimeSpan.FromMinutes(1),
            },
        };

        var registered = await agent.RegisterAsync(registration, ct).RecordAsync("rates.consul_register").ConfigureAwait(false);
        if (registered.TryGetError(out var error))
        {
            LogRegistrationFailed(instance.Id, error.Code);
            return false;
        }

        LogRegistered(instance.Id, instance.Address);
        return true;
    }

    // Returns whether Consul still knows the instance. Only a lost registration starts over; an unreachable agent is
    // asked again at the next beat, as is one that gave an unexpected answer.
    private async Task<bool> HeartbeatAsync(CancellationToken ct)
    {
        var healthy = !outage.IsActive;
        var reported = await agent.ReportAsync(instance.CheckId, healthy, healthy ? "Serving rates." : "Outage switched on.", ct)
            .RecordAsync("rates.consul_heartbeat")
            .ConfigureAwait(false);

        if (!reported.TryGetError(out var error))
            return true;

        LogHeartbeatFailed(instance.Id, error.Code);
        return error.Code != ConsulAgent.Errors.RegistrationLost.Code;
    }

    private async Task StartedAsync(CancellationToken stoppingToken)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var onStarted = lifetime.ApplicationStarted.Register(() => started.TrySetResult());
        await started.Task.WaitAsync(stoppingToken).ConfigureAwait(false);
    }

    [LoggerMessage(LogLevel.Information, "Registered {Id} in Consul at {Address}.")]
    private partial void LogRegistered(string id, Uri address);

    [LoggerMessage(LogLevel.Warning, "Could not register {Id} in Consul ({Code}); trying again at the next heartbeat.")]
    private partial void LogRegistrationFailed(string id, string code);

    [LoggerMessage(LogLevel.Warning, "The heartbeat of {Id} failed ({Code}).")]
    private partial void LogHeartbeatFailed(string id, string code);

    [LoggerMessage(LogLevel.Information, "Deregistered {Id} from Consul.")]
    private partial void LogDeregistered(string id);

    [LoggerMessage(LogLevel.Warning, "Could not deregister {Id} from Consul ({Code}); it drops out after its check's TTL.")]
    private partial void LogDeregistrationFailed(string id, string code);
}
