using ResultUnion.Consul.Rates.Registration;

namespace ResultUnion.Consul.Rates.Rates;

public sealed class RateService(RateTable table, Outage outage, RatesInstance instance)
{
    public Result<Rate> Find(string? from, string? to) =>
        outage.Check(instance.Id)
            .Bind(() => Result.CombineAll(
                Currency.Create(from).MapError(e => e.WithField("from")),
                Currency.Create(to).MapError(e => e.WithField("to"))))
            .Bind(table.Find)
            .Record("rates.find");
}
