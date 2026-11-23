namespace ResultUnion.Consul.Rates.Rates;

/// <summary>
/// A switch for the demo: while it is on, this instance answers every rate request with 503 and reports its Consul
/// check as critical at the next heartbeat, so the converter first meets the failure and then stops being sent here.
/// </summary>
public sealed class Outage
{
    public static class Errors
    {
        public static Error Active(string instance) =>
            Error.Create("rates.outage", $"The rates instance {instance} is having an outage.", ErrorType.Unavailable)
                .WithArgument("instance", instance);
    }

    private volatile bool _active;

    public bool IsActive => _active;

    public void Start() => _active = true;

    public void End() => _active = false;

    public Result Check(string instance) => _active ? Errors.Active(instance) : Result.Success();
}
