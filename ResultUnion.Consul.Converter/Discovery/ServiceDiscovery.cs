using Consul;

namespace ResultUnion.Consul.Converter.Discovery;

/// <summary>
/// Finds a service's instances in Consul's catalog. Only instances whose checks pass are asked for, so an instance
/// that stopped its heartbeat, or reports an outage, is not among them. No instance at all is a failure in itself:
/// the service is down as far as this one can tell, and a client should try again later.
/// </summary>
public sealed class ServiceDiscovery(IConsulClient consul)
{
    public static class Errors
    {
        public static Error NoHealthyInstance(string service) =>
            Error.Create("discovery.no_healthy_instance", $"No healthy instance of {service} is registered in Consul.", ErrorType.Unavailable)
                .WithArgument("service", service);
    }

    /// <summary>The healthy instances' addresses, in random order, so the calls spread over them.</summary>
    public Task<Result<IReadOnlyList<Uri>>> FindAsync(string service, CancellationToken ct) =>
        ConsulCall.RunAsync(() => consul.Health.Service(service, tag: null, passingOnly: true, ct), ct)
            .BindAsync(answer => Addresses(service, answer.Response ?? []));

    private static Result<IReadOnlyList<Uri>> Addresses(string service, ServiceEntry[] entries)
    {
        if (entries.Length == 0)
            return Errors.NoHealthyInstance(service);

        // An instance registered without an address of its own is at its node's address, as Consul's DNS answers it.
        Uri[] addresses = [.. entries.Select(entry => new UriBuilder(
            Uri.UriSchemeHttp,
            string.IsNullOrEmpty(entry.Service.Address) ? entry.Node.Address : entry.Service.Address,
            entry.Service.Port).Uri)];
        Random.Shared.Shuffle(addresses);
        return addresses;
    }
}
