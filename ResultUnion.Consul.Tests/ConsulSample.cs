extern alias converter;
extern alias rates;

using System.Collections.Concurrent;
using Consul;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ConverterProgram = converter::Program;
using RatesClient = converter::ResultUnion.Consul.Converter.Rates.RatesClient;
using RatesProgram = rates::Program;

namespace ResultUnion.Consul.Tests;

/// <summary>
/// The sample in memory: one <see cref="FakeConsul"/>, any number of rates instances that register in it, and the
/// converter. Each rates instance registers under the host name it is reached by (<c>http://rates-a</c>), and the
/// converter's HTTP client sends each call to the test server of the instance Consul gave it. A host no server answers
/// for is a refused connection.
/// </summary>
public sealed class ConsulSample : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, WebApplicationFactory<RatesProgram>> _rates = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, HttpMessageInvoker> _routes = new(StringComparer.Ordinal);
    private readonly WebApplicationFactory<ConverterProgram> _converter;

    public ConsulSample()
    {
        _converter = new WebApplicationFactory<ConverterProgram>().WithWebHostBuilder(host => host
            .UseSetting("OTEL_SDK_DISABLED", "true")
            .ConfigureServices(services =>
            {
                services.Replace(ServiceDescriptor.Singleton(_ => Consul.CreateClient()));
                services.AddHttpClient<RatesClient>().ConfigurePrimaryHttpMessageHandler(() => new Router(_routes));
            }));
        Converter = _converter.CreateClient();
    }

    public FakeConsul Consul { get; } = new();

    public HttpClient Converter { get; }

    /// <summary>Starts a rates instance that registers as <paramref name="id"/>, with a heartbeat every 50 ms.</summary>
    public HttpClient StartRates(string id)
    {
        var factory = new WebApplicationFactory<RatesProgram>().WithWebHostBuilder(host => host
            .UseSetting("OTEL_SDK_DISABLED", "true")        // no collector to flush to when a host stops
            .UseSetting("Consul:ServiceId", id)
            .UseSetting("Consul:ServiceAddress", $"http://{id}")
            .UseSetting("Consul:Heartbeat", "00:00:00.050")
            .ConfigureServices(services => services.Replace(ServiceDescriptor.Singleton(_ => Consul.CreateClient()))));
        _rates[id] = factory;
        _routes[id] = new HttpMessageInvoker(factory.Server.CreateHandler());
        return factory.CreateClient();
    }

    /// <summary>Stops an instance the way a host stops: it deregisters itself.</summary>
    public async Task StopRatesAsync(string id)
    {
        _routes.TryRemove(id, out _);
        if (_rates.TryRemove(id, out var factory))
            await factory.DisposeAsync();
    }

    /// <summary>Makes an instance unreachable without telling Consul, as a network failure would.</summary>
    public void Unplug(string id) => _routes.TryRemove(id, out _);

    /// <summary>Waits until Consul lists exactly <paramref name="ids"/> as passing instances of the rates service.</summary>
    public async Task WaitUntilPassingAsync(params string[] ids)
    {
        var expected = ids.Order(StringComparer.Ordinal).ToArray();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        while (!Consul.Passing("rates").SequenceEqual(expected))
            await Task.Delay(20, timeout.Token);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var id in _rates.Keys)
            await StopRatesAsync(id);

        await _converter.DisposeAsync();
    }

    private sealed class Router(ConcurrentDictionary<string, HttpMessageInvoker> routes) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            routes.TryGetValue(request.RequestUri!.Host, out var server)
                ? server.SendAsync(request, cancellationToken)
                : throw new HttpRequestException($"Connection refused ({request.RequestUri.Authority})");
    }
}
