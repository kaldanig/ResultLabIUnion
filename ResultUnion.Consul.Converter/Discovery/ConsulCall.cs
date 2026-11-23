using System.Net;
using Consul;

namespace ResultUnion.Consul.Converter.Discovery;

/// <summary>
/// One call to the Consul agent, with every way it can fail as an error: an agent that cannot be reached (a refused
/// connection, the client's own timeout) is worth a retry later; any other failed answer is a bug on one side.
/// </summary>
public static class ConsulCall
{
    public static class Errors
    {
        public static readonly Error Unreachable =
            Error.Create("consul.unreachable", "The Consul agent cannot be reached; try again shortly.", ErrorType.Unavailable);

        public static Error UnexpectedAnswer(HttpStatusCode status) =>
            Error.Create("consul.unexpected_answer", $"The Consul agent answered {(int)status}.", ErrorType.Unexpected)
                .WithArgument("status", (int)status);
    }

    // The one place with a hand-written catch, as in any adapter over a client that throws: the caller's cancellation
    // still propagates. The client returns a 404 instead of throwing it; for a read that means "nothing there", which
    // the caller sees as an empty Response.
    public static async Task<Result<T>> RunAsync<T>(Func<Task<T>> call, CancellationToken ct)
        where T : ConsulResult
    {
        try
        {
            return await call().ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            return Errors.Unreachable;
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return Errors.Unreachable;
        }
        catch (ConsulRequestException ex)
        {
            return Errors.UnexpectedAnswer(ex.StatusCode);
        }
    }
}
