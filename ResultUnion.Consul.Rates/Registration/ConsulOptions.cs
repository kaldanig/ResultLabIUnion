namespace ResultUnion.Consul.Rates.Registration;

/// <summary>The <c>Consul</c> section of the configuration.</summary>
public sealed class ConsulOptions
{
    /// <summary>The Consul agent's HTTP API.</summary>
    public Uri Address { get; set; } = new("http://localhost:8500");

    /// <summary>The name the converter looks this service up by.</summary>
    public string ServiceName { get; set; } = "rates";

    /// <summary>This instance's id in Consul; by default the service name and the port, so a restart replaces the old entry.</summary>
    public string? ServiceId { get; set; }

    /// <summary>The address to register; by default the one the server listens on, with a wildcard host as <c>localhost</c>.</summary>
    public Uri? ServiceAddress { get; set; }

    /// <summary>How often the instance tells Consul it is alive.</summary>
    public TimeSpan Heartbeat { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>How long Consul waits for a heartbeat before it marks the instance critical and stops listing it.</summary>
    public TimeSpan CheckTtl { get; set; } = TimeSpan.FromSeconds(10);
}
