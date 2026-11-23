using System.Net;
using Consul;

namespace ResultUnion.Consul.Rates.Registration;

/// <summary>
/// The local Consul agent, as this instance uses it: register, report the TTL check, deregister. The Consul client
/// throws for every failure; this adapter turns each into an error, so the registration loop decides on the code:
/// <list type="bullet">
///   <item><description>an agent that cannot be reached is worth another try at the next heartbeat;</description></item>
///   <item><description>a check the agent does not know means it forgot this instance (a dev agent restarted): register again;</description></item>
///   <item><description>any other answer is a bug on one side.</description></item>
/// </list>
/// </summary>
public sealed class ConsulAgent(IConsulClient consul)
{
    public static class Errors
    {
        public static readonly Error Unreachable =
            Error.Create("consul.unreachable", "The Consul agent cannot be reached.", ErrorType.Unavailable);

        public static readonly Error RegistrationLost =
            Error.Create("consul.registration_lost", "The Consul agent does not know this instance's check.", ErrorType.NotFound);

        public static Error UnexpectedAnswer(HttpStatusCode status) =>
            Error.Create("consul.unexpected_answer", $"The Consul agent answered {(int)status}.", ErrorType.Unexpected)
                .WithArgument("status", (int)status);
    }

    public Task<Result> RegisterAsync(AgentServiceRegistration registration, CancellationToken ct) =>
        CallAsync(() => consul.Agent.ServiceRegister(registration, ct), ct);

    public Task<Result> ReportAsync(string checkId, bool healthy, string note, CancellationToken ct) =>
        CallAsync(() => consul.Agent.UpdateTTL(checkId, note, healthy ? TTLStatus.Pass : TTLStatus.Critical, ct), ct);

    public Task<Result> DeregisterAsync(string serviceId, CancellationToken ct) =>
        CallAsync(() => consul.Agent.ServiceDeregister(serviceId, ct), ct);

    // The one place with a hand-written catch: a refused connection and the client's own timeout are the same outcome,
    // while the caller's cancellation (the host stopping) still propagates. The client throws for every failed status
    // but 404, which it returns as the result's status instead.
    private static async Task<Result> CallAsync(Func<Task<WriteResult>> call, CancellationToken ct)
    {
        try
        {
            var answer = await call().ConfigureAwait(false);
            return answer.StatusCode == HttpStatusCode.NotFound ? Errors.RegistrationLost : Result.Success();
        }
        catch (HttpRequestException)
        {
            return Errors.Unreachable;
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return Errors.Unreachable;
        }
        catch (ConsulRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return Errors.RegistrationLost;
        }
        catch (ConsulRequestException ex)
        {
            return Errors.UnexpectedAnswer(ex.StatusCode);
        }
    }
}
