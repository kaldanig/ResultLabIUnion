using System.Net;

namespace ResultUnion.Consul.Tests;

/// <summary>A rates instance's life in Consul: registered once it listens, kept passing, registered again, deregistered.</summary>
public sealed class RegistrationTests : IAsyncLifetime
{
    private readonly ConsulSample _sample = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => _sample.DisposeAsync();

    [Fact]
    public async Task An_instance_registers_and_its_heartbeat_makes_its_check_pass()
    {
        _sample.StartRates("rates-a");

        await _sample.WaitUntilPassingAsync("rates-a");
    }

    // A dev agent keeps its registrations in memory. The next heartbeat is a 404 for the unknown check, which the
    // instance answers by registering again.
    [Fact]
    public async Task An_instance_registers_again_when_Consul_forgot_it()
    {
        _sample.StartRates("rates-a");
        await _sample.WaitUntilPassingAsync("rates-a");

        _sample.Consul.Forget();

        await _sample.WaitUntilPassingAsync("rates-a");
    }

    [Fact]
    public async Task An_instance_that_stops_deregisters()
    {
        _sample.StartRates("rates-a");
        _sample.StartRates("rates-b");
        await _sample.WaitUntilPassingAsync("rates-a", "rates-b");

        await _sample.StopRatesAsync("rates-a");

        Assert.Equal(["rates-b"], _sample.Consul.Registered("rates"));
    }

    // Consul is how the converter finds an instance, not something the instance needs to answer.
    [Fact]
    public async Task An_instance_answers_while_Consul_is_down_and_registers_once_it_is_back()
    {
        _sample.Consul.IsDown = true;
        var rates = _sample.StartRates("rates-a");

        using var answer = await rates.GetAsync("/rates/usd/eur", Ct);
        _sample.Consul.IsDown = false;
        await _sample.WaitUntilPassingAsync("rates-a");

        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
    }
}
