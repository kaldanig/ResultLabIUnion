using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Options;

namespace ResultUnion.Consul.Rates.Registration;

/// <summary>
/// This instance as Consul knows it: its id and the address the converter should call. The address is the server's
/// own, so it is only known once the server listens (under Aspire, each replica gets a port of its own).
/// </summary>
public sealed class RatesInstance
{
    private readonly Lazy<Uri> _address;
    private readonly Lazy<string> _id;

    public RatesInstance(IServer server, IOptions<ConsulOptions> options)
    {
        var consul = options.Value;
        _address = new(() => consul.ServiceAddress ?? Listening(server));
        _id = new(() => consul.ServiceId ?? $"{consul.ServiceName}-{Address.Port}");
    }

    public Uri Address => _address.Value;

    public string Id => _id.Value;

    /// <summary>The id of the TTL check Consul creates with the registration.</summary>
    public string CheckId => $"service:{Id}";

    private static Uri Listening(IServer server)
    {
        var addresses = (server.Features.Get<IServerAddressesFeature>()?.Addresses ?? []).Select(BindingAddress.Parse).ToList();
        var address = addresses.FirstOrDefault(a => a.Scheme == Uri.UriSchemeHttp) ?? addresses.FirstOrDefault()
            ?? throw new InvalidOperationException("The server listens on no address yet; set Consul:ServiceAddress.");
        var host = address.Host is "+" or "*" or "0.0.0.0" or "[::]" ? "localhost" : address.Host;
        return new UriBuilder(address.Scheme, host, address.Port).Uri;
    }
}
