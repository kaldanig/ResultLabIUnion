using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace ResultUnion.Consul.Tests;

/// <summary>The converter as a client sees it, with the rates instances it finds in Consul.</summary>
public sealed class ConversionTests : IAsyncLifetime
{
    private readonly ConsulSample _sample = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => _sample.DisposeAsync();

    [Fact]
    public async Task A_conversion_is_answered_by_an_instance_Consul_lists_with_the_fee_from_its_key_value_store()
    {
        _sample.StartRates("rates-a");
        _sample.StartRates("rates-b");
        _sample.Consul.Put("converter/fee-percent", "1.5");
        await _sample.WaitUntilPassingAsync("rates-a", "rates-b");

        var conversion = await _sample.Converter.GetFromJsonAsync<JsonElement>("/convert?amount=100&from=usd&to=gel", Ct);

        Assert.Equal(2.71m, conversion.GetProperty("rate").GetDecimal());
        Assert.Equal(1.5m, conversion.GetProperty("feePercent").GetDecimal());
        Assert.Equal(4.07m, conversion.GetProperty("fee").GetDecimal());
        Assert.Equal(266.93m, conversion.GetProperty("converted").GetDecimal());
        Assert.Contains(conversion.GetProperty("servedBy").GetString(), new[] { "rates-a", "rates-b" });
    }

    [Fact]
    public async Task Every_invalid_input_is_reported_at_once_before_Consul_is_asked()
    {
        _sample.Consul.IsDown = true;

        var problem = await ProblemAsync("/convert?amount=-1&from=us", HttpStatusCode.BadRequest);

        Assert.Equal(
            [("amount", "amount.not_positive"), ("from", "currency.invalid_format"), ("to", "currency.required")],
            problem.GetProperty("details").EnumerateArray()
                .Select(d => (d.GetProperty("field").GetString(), d.GetProperty("code").GetString())));
    }

    [Theory]
    [InlineData("/convert?amount=5&from=usd&to=xyz", "to", "XYZ")]
    [InlineData("/convert?amount=5&from=abc&to=usd", "from", "ABC")]
    public async Task A_currency_without_a_rate_is_the_converters_own_404_on_the_field_that_named_it(string url, string field, string currency)
    {
        _sample.StartRates("rates-a");
        await _sample.WaitUntilPassingAsync("rates-a");

        var problem = await ProblemAsync(url, HttpStatusCode.NotFound);

        Assert.Equal("conversion.unknown_currency", problem.GetProperty("code").GetString());
        Assert.Equal(currency, problem.GetProperty("arguments").GetProperty("currency").GetString());
        Assert.True(problem.GetProperty("errors").TryGetProperty(field, out _));
    }

    // Consul still lists the instance (its heartbeat goes on), but the converter's calls cannot reach it: a transient
    // failure, so every conversion goes to the other instance.
    [Fact]
    public async Task An_instance_that_cannot_be_reached_is_skipped_for_the_next_one()
    {
        _sample.StartRates("rates-a");
        _sample.StartRates("rates-b");
        await _sample.WaitUntilPassingAsync("rates-a", "rates-b");
        _sample.Unplug("rates-a");

        for (var i = 0; i < 10; i++)
        {
            var conversion = await _sample.Converter.GetFromJsonAsync<JsonElement>("/convert?amount=1&from=usd&to=eur", Ct);
            Assert.Equal("rates-b", conversion.GetProperty("servedBy").GetString());
        }
    }

    [Fact]
    public async Task An_instance_in_an_outage_answers_503_reports_its_check_critical_and_is_listed_again_when_it_ends()
    {
        var ratesA = _sample.StartRates("rates-a");
        _sample.StartRates("rates-b");
        await _sample.WaitUntilPassingAsync("rates-a", "rates-b");

        (await ratesA.PostAsync("/outage", null, Ct)).EnsureSuccessStatusCode();
        using var refused = await ratesA.GetAsync("/rates/usd/eur", Ct);
        await _sample.WaitUntilPassingAsync("rates-b");
        var instances = await _sample.Converter.GetFromJsonAsync<string[]>("/instances", Ct) ?? [];

        (await ratesA.DeleteAsync("/outage", Ct)).EnsureSuccessStatusCode();
        await _sample.WaitUntilPassingAsync("rates-a", "rates-b");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, refused.StatusCode);
        Assert.Equal("rates.outage", (await refused.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString());
        Assert.Equal(["http://rates-b/"], instances);
    }

    [Fact]
    public async Task Every_instance_failing_is_a_503_with_the_last_instances_error()
    {
        _sample.StartRates("rates-a");
        await _sample.WaitUntilPassingAsync("rates-a");
        _sample.Unplug("rates-a");

        var problem = await ProblemAsync("/convert?amount=1&from=usd&to=eur", HttpStatusCode.ServiceUnavailable);

        Assert.Equal("rates.unavailable", problem.GetProperty("code").GetString());
        Assert.Equal("http://rates-a/", problem.GetProperty("arguments").GetProperty("instance").GetString());
    }

    [Fact]
    public async Task No_healthy_instance_in_Consul_is_a_503()
    {
        var problem = await ProblemAsync("/convert?amount=1&from=usd&to=eur", HttpStatusCode.ServiceUnavailable);

        Assert.Equal("discovery.no_healthy_instance", problem.GetProperty("code").GetString());
        Assert.Equal("rates", problem.GetProperty("arguments").GetProperty("service").GetString());
    }

    [Fact]
    public async Task A_Consul_agent_that_cannot_be_reached_is_a_503()
    {
        _sample.Consul.IsDown = true;

        var problem = await ProblemAsync("/convert?amount=1&from=usd&to=eur", HttpStatusCode.ServiceUnavailable);

        Assert.Equal("consul.unreachable", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task The_pause_switch_in_the_key_value_store_refuses_conversions_with_a_503()
    {
        _sample.StartRates("rates-a");
        await _sample.WaitUntilPassingAsync("rates-a");
        _sample.Consul.Put("converter/paused", "true");

        var problem = await ProblemAsync("/convert?amount=1&from=usd&to=eur", HttpStatusCode.ServiceUnavailable);

        Assert.Equal("conversion.paused", problem.GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("converter/fee-percent", "lots")]
    [InlineData("converter/fee-percent", "100")]
    [InlineData("converter/paused", "maybe")]
    public async Task A_setting_that_holds_an_invalid_value_is_an_unexpected_error(string key, string value)
    {
        _sample.Consul.Put(key, value);

        var problem = await ProblemAsync("/convert?amount=1&from=usd&to=eur", HttpStatusCode.InternalServerError);

        Assert.Equal("settings.invalid", problem.GetProperty("code").GetString());
    }

    private async Task<JsonElement> ProblemAsync(string url, HttpStatusCode status)
    {
        using var response = await _sample.Converter.GetAsync(url, Ct);
        Assert.Equal(status, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }
}
