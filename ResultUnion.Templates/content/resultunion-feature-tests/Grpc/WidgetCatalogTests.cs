using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Mvc.Testing;
#if (Crud)
using Microsoft.Extensions.DependencyInjection;
using APP_NAMESPACE.Gizmos;
#endif
using APP_NAMESPACE.Gizmos.V1;
using ResultUnion;
using ResultUnion.Grpc;
using ResultUnion.Testing;
using Xunit;

namespace TargetNamespace.Gizmos;

/// <summary>
/// The application in memory, called through the generated gRPC client: what a client receives, failures included. A
/// failed call is read back as an <see cref="Error"/> with <c>ToError()</c>, with the same codes and fields.
/// </summary>
public sealed class WidgetCatalogTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly WebApplicationFactory<Program> _app;
    private readonly GrpcChannel _channel;
    private readonly WidgetCatalog.WidgetCatalogClient _client;

    public WidgetCatalogTests(WebApplicationFactory<Program> factory)
    {
#if (Sqlite)
        // A database file of its own, so the tests never touch the application's. "Products" is the connection string
        // of resultunion-grpc --database sqlite; use the application's own name.
        _app = factory.WithWebHostBuilder(host =>
            host.UseSetting("ConnectionStrings:Products", $"Data Source={Path.Combine(Path.GetTempPath(), $"PLURAL_ROUTE-{Guid.NewGuid():N}.db")}"));
#else
        _app = factory;
#endif
        _channel = GrpcChannel.ForAddress(_app.Server.BaseAddress, new GrpcChannelOptions { HttpHandler = _app.Server.CreateHandler() });
        _client = new WidgetCatalog.WidgetCatalogClient(_channel);
    }

    public void Dispose() => _channel.Dispose();

    [Fact]
    public async Task A_created_ENTITY_CODE_can_be_read_back()
    {
        var created = await _client.CreateWidgetAsync(new CreateWidgetRequest { Name = $"First {Guid.NewGuid()}" }, cancellationToken: Ct);

        var found = await _client.GetWidgetAsync(new GetWidgetRequest { Id = created.Id }, cancellationToken: Ct);

        Assert.Equal(created, found);
    }

    [Fact]
    public async Task An_empty_name_is_INVALID_ARGUMENT_with_its_code_and_field()
    {
        var error = await FailureOf(() => _client.CreateWidgetAsync(new CreateWidgetRequest { Name = " " }, cancellationToken: Ct).ResponseAsync, StatusCode.InvalidArgument);

        Assert.Equal("ENTITY_CODE_name.required", error.Code);
        Assert.Equal("name", error.Field);
    }

    [Fact]
    public async Task A_name_that_is_taken_is_FAILED_PRECONDITION()
    {
        var name = $"First {Guid.NewGuid()}";
        await _client.CreateWidgetAsync(new CreateWidgetRequest { Name = name }, cancellationToken: Ct);

        var error = await FailureOf(() => _client.CreateWidgetAsync(new CreateWidgetRequest { Name = name.ToUpperInvariant() }, cancellationToken: Ct).ResponseAsync, StatusCode.FailedPrecondition);

        Assert.Equal("ENTITY_CODE.name_taken", error.Code);
    }

    [Fact]
    public async Task A_missing_ENTITY_CODE_is_NOT_FOUND()
    {
        var error = await FailureOf(() => _client.GetWidgetAsync(new GetWidgetRequest { Id = Guid.NewGuid().ToString() }, cancellationToken: Ct).ResponseAsync, StatusCode.NotFound);

        Assert.Equal("ENTITY_CODE.not_found", error.Code);
    }

    [Fact]
    public async Task An_id_that_is_not_a_guid_is_INVALID_ARGUMENT_on_the_id_field()
    {
        var error = await FailureOf(() => _client.GetWidgetAsync(new GetWidgetRequest { Id = "42" }, cancellationToken: Ct).ResponseAsync, StatusCode.InvalidArgument);

        Assert.Equal("ENTITY_CODE.invalid_id", error.Code);
        Assert.Equal("id", error.Field);
    }

#if (Crud)
    [Fact]
    public async Task A_rename_returns_the_next_version_and_a_stale_one_is_ABORTED()
    {
        var created = await _client.CreateWidgetAsync(new CreateWidgetRequest { Name = $"First {Guid.NewGuid()}" }, cancellationToken: Ct);

        var renamed = await _client.RenameWidgetAsync(new RenameWidgetRequest { Id = created.Id, Name = $"Second {Guid.NewGuid()}", Version = created.Version }, cancellationToken: Ct);
        Assert.Equal(created.Version + 1, renamed.Version);

        var error = await FailureOf(() => _client.RenameWidgetAsync(new RenameWidgetRequest { Id = created.Id, Name = $"Third {Guid.NewGuid()}", Version = created.Version }, cancellationToken: Ct).ResponseAsync, StatusCode.Aborted);
        Assert.Equal("ENTITY_CODE.changed_concurrently", error.Code);
    }

    [Fact]
    public async Task A_removed_ENTITY_CODE_is_NOT_FOUND_afterwards()
    {
        var created = await _client.CreateWidgetAsync(new CreateWidgetRequest { Name = $"First {Guid.NewGuid()}" }, cancellationToken: Ct);

        await _client.RemoveWidgetAsync(new RemoveWidgetRequest { Id = created.Id, Version = created.Version }, cancellationToken: Ct);

        var error = await FailureOf(() => _client.RemoveWidgetAsync(new RemoveWidgetRequest { Id = created.Id, Version = created.Version }, cancellationToken: Ct).ResponseAsync, StatusCode.NotFound);
        Assert.Equal("ENTITY_CODE.not_found", error.Code);
    }

    [Fact]
    public async Task A_rename_to_a_taken_name_is_FAILED_PRECONDITION()
    {
        var taken = $"First {Guid.NewGuid()}";
        await _client.CreateWidgetAsync(new CreateWidgetRequest { Name = taken }, cancellationToken: Ct);
        var other = await _client.CreateWidgetAsync(new CreateWidgetRequest { Name = $"Second {Guid.NewGuid()}" }, cancellationToken: Ct);

        var error = await FailureOf(() => _client.RenameWidgetAsync(new RenameWidgetRequest { Id = other.Id, Name = taken.ToUpperInvariant(), Version = other.Version }, cancellationToken: Ct).ResponseAsync, StatusCode.FailedPrecondition);

        Assert.Equal("ENTITY_CODE.name_taken", error.Code);
    }

    // Two calls read the same version; the second one's write comes after the first one's. The service's check saw the
    // version it read, so only the store, which compares as it writes, can refuse it.
    [Fact]
    public async Task The_store_refuses_a_write_from_a_version_that_changed_after_it_was_read()
    {
        var created = await _client.CreateWidgetAsync(new CreateWidgetRequest { Name = $"First {Guid.NewGuid()}" }, cancellationToken: Ct);
        var id = Guid.Parse(created.Id);
        using var scope = _app.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IWidgetRepository>();
        var read = await repository.FindAsync(id, Ct) ?? throw new InvalidOperationException("Not stored.");

        await _client.RenameWidgetAsync(new RenameWidgetRequest { Id = created.Id, Name = $"Second {Guid.NewGuid()}", Version = read.Version }, cancellationToken: Ct);

        var changed = read.Rename(WidgetName.Create($"Third {Guid.NewGuid()}").ShouldSucceed());
        await repository.UpdateAsync(changed, read.Version, Ct).ShouldFailAsync("ENTITY_CODE.changed_concurrently");
        await repository.RemoveAsync(id, read.Version, Ct).ShouldFailAsync("ENTITY_CODE.changed_concurrently");
    }

#endif
    // Only an RpcException is read back; it carries the error's code and field in its status details.
    private static async Task<Error> FailureOf<T>(Func<Task<T>> call, StatusCode expected)
    {
        var exception = await Assert.ThrowsAsync<RpcException>(call);
        Assert.Equal(expected, exception.StatusCode);
        return exception.ToError();
    }
}
