using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ResultUnion.AppHost.Tests;

/// <summary>Starts the AppHost once for the class: three processes, so starting it per test would be slow.</summary>
public sealed class AppHostFixture : IAsyncLifetime
{
    private static readonly TimeSpan StartTimeout = TimeSpan.FromMinutes(2);

    public DistributedApplication App { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.ResultUnion_AppHost>();

        // Every console line of the three demos is logged at Information; a failed test still shows their warnings.
        builder.Services.AddLogging(logging => logging.AddFilter($"{builder.Environment.ApplicationName}.Resources", LogLevel.Warning));

        App = await builder.BuildAsync().WaitAsync(StartTimeout);
        await App.StartAsync().WaitAsync(StartTimeout);
    }

    public async ValueTask DisposeAsync()
    {
        if (App is not null)
            await App.DisposeAsync();
    }
}

public sealed class AppHostTests(AppHostFixture fixture) : IClassFixture<AppHostFixture>
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromMinutes(2);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_web_api_answers_with_a_problem_and_its_code()
    {
        await fixture.App.ResourceNotifications.WaitForResourceAsync("webapi", KnownResourceStates.Running, Ct).WaitAsync(WaitTimeout, Ct);
        using var http = fixture.App.CreateHttpClient("webapi", "http");

        using var response = await http.PostAsJsonAsync("/users", new { email = "", password = "x", age = "abc" }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("validation.failed", problem.GetProperty("code").GetString());
    }

    // The client runs every call of the gRPC demo against the service and exits; a call that the service could not
    // answer (a connection refused, an exception that is not an RpcException) would end it with a failure exit code.
    [Fact]
    public async Task The_grpc_client_runs_every_call_against_the_service_and_exits_cleanly()
    {
        var finished = await fixture.App.ResourceNotifications
            .WaitForResourceAsync("grpcclient", e => e.Snapshot.State?.Text == KnownResourceStates.Finished, Ct)
            .WaitAsync(WaitTimeout, Ct);

        Assert.Equal(0, finished.Snapshot.ExitCode);
    }
}
