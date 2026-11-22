using APP_NAMESPACE.Gizmos;
using ResultUnion;
using ResultUnion.Testing;
using Xunit;

namespace TargetNamespace.Gizmos;

/// <summary>
/// The use cases without a host, one test for each code they can return. The assertions return the value or the
/// error, so a test reads the outcome in one line and asserts on codes and fields, never on messages.
/// </summary>
public sealed class WidgetServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly WidgetService _service = new(new InMemoryWidgetRepository());

    [Fact]
    public async Task A_valid_ENTITY_CODE_is_created()
    {
        var created = await _service.CreateAsync(new CreateWidget("  First "), Ct).ShouldSucceedAsync();

        Assert.Equal("First", created.Name.Value);
    }

    [Fact]
    public async Task An_empty_name_is_reported_for_its_field()
    {
        var error = await _service.CreateAsync(new CreateWidget(" "), Ct).ShouldFailAsync("ENTITY_CODE_name.required");

        Assert.Equal("name", error.Field);
    }

    [Fact]
    public async Task A_name_that_is_taken_is_a_conflict()
    {
        await _service.CreateAsync(new CreateWidget("First"), Ct).ShouldSucceedAsync();

        var error = await _service.CreateAsync(new CreateWidget("FIRST"), Ct).ShouldFailAsync("ENTITY_CODE.name_taken");

        error.ShouldHaveArgument("name", "FIRST");
    }

    [Fact]
    public async Task A_missing_ENTITY_CODE_is_not_found()
    {
        var id = Guid.NewGuid();

        var error = await _service.FindAsync(id, Ct).ShouldFailAsync("ENTITY_CODE.not_found");

        error.ShouldHaveArgument("id", id);
    }
#if (Crud)

    [Fact]
    public async Task A_rename_gives_the_next_version()
    {
        var created = await _service.CreateAsync(new CreateWidget("First"), Ct).ShouldSucceedAsync();

        var renamed = await _service.RenameAsync(new RenameWidget(created.Id, "Second", created.Version), Ct).ShouldSucceedAsync();

        Assert.Equal("Second", renamed.Name.Value);
        Assert.Equal(created.Version + 1, renamed.Version);
    }

    [Fact]
    public async Task A_rename_from_a_stale_version_changed_concurrently()
    {
        var created = await _service.CreateAsync(new CreateWidget("First"), Ct).ShouldSucceedAsync();
        await _service.RenameAsync(new RenameWidget(created.Id, "Second", created.Version), Ct).ShouldSucceedAsync();

        var error = await _service.RenameAsync(new RenameWidget(created.Id, "Third", created.Version), Ct)
            .ShouldFailAsync("ENTITY_CODE.changed_concurrently");

        error.ShouldHaveArgument("version", created.Version);
    }

    [Fact]
    public async Task A_rename_to_a_taken_name_is_a_conflict()
    {
        await _service.CreateAsync(new CreateWidget("First"), Ct).ShouldSucceedAsync();
        var second = await _service.CreateAsync(new CreateWidget("Second"), Ct).ShouldSucceedAsync();

        await _service.RenameAsync(new RenameWidget(second.Id, "first", second.Version), Ct).ShouldFailAsync("ENTITY_CODE.name_taken");
    }

    [Fact]
    public async Task A_removed_ENTITY_CODE_is_not_found()
    {
        var created = await _service.CreateAsync(new CreateWidget("First"), Ct).ShouldSucceedAsync();

        await _service.RemoveAsync(created.Id, created.Version, Ct).ShouldSucceedAsync();

        await _service.FindAsync(created.Id, Ct).ShouldFailAsync("ENTITY_CODE.not_found");
    }

    [Fact]
    public async Task A_remove_from_a_stale_version_changed_concurrently()
    {
        var created = await _service.CreateAsync(new CreateWidget("First"), Ct).ShouldSucceedAsync();
        await _service.RenameAsync(new RenameWidget(created.Id, "Second", created.Version), Ct).ShouldSucceedAsync();

        await _service.RemoveAsync(created.Id, created.Version, Ct).ShouldFailAsync("ENTITY_CODE.changed_concurrently");
    }
#endif
}
