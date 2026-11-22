using Grpc.Net.Client;
#if (Sqlite)
using Microsoft.AspNetCore.Hosting;
#endif
using Microsoft.AspNetCore.Mvc.Testing;

namespace ResultUnionGrpc.Tests;

/// <summary>
/// The service in memory, for the tests that call it through a gRPC client. The channel goes through the in-memory
/// server, so no port is opened.
/// </summary>
public sealed class ServiceFactory : WebApplicationFactory<Program>
{
#if (Sqlite)
    // A database file of its own, so the tests never touch the application's.
    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.UseSetting("ConnectionStrings:Products", $"Data Source={Path.Combine(Path.GetTempPath(), $"products-{Guid.NewGuid():N}.db")}");

#endif
    public GrpcChannel CreateChannel() =>
        GrpcChannel.ForAddress(Server.BaseAddress, new GrpcChannelOptions { HttpHandler = Server.CreateHandler() });
}
