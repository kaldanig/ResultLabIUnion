using ResultUnion;

namespace TargetNamespace.Gizmos;

/// <summary>The raw input of "create a ENTITY_WORDS", from any host (HTTP here, a message or a test elsewhere).</summary>
public sealed record CreateWidget(string? Name);
#if (Crud)

/// <summary>The raw input of "rename a ENTITY_WORDS", with the version the client read.</summary>
public sealed record RenameWidget(Guid Id, string? Name, int Version);
#endif

/// <summary>The use cases. Each one is a single pipeline that runs while every step succeeds.</summary>
public sealed class WidgetService(IWidgetRepository repository)
{
    public Task<Result<Widget>> CreateAsync(CreateWidget command, CancellationToken ct) =>
        WidgetName.Create(command.Name)
            .MapError(e => e.WithField("name"))  // the request calls it "name"
            .Map(Widget.Create)
            .EnsureAsync(entity => repository.AddAsync(entity, ct))  // a taken name fails here
            .RecordAsync("PLURAL_CODE.create");  // failures counted by code

    public Task<Result<Widget>> FindAsync(Guid id, CancellationToken ct) =>
        repository.FindAsync(id, ct)
            .ToResultAsync(Widget.Errors.NotFound(id))  // null becomes the catalog error
            .RecordAsync("PLURAL_CODE.find");
#if (Crud)

    public Task<Result<Widget>> RenameAsync(RenameWidget command, CancellationToken ct) =>
        WidgetName.Create(command.Name)
            .MapError(e => e.WithField("name"))
            .BindAsync(name => repository.FindAsync(command.Id, ct)
                .ToResultAsync(Widget.Errors.NotFound(command.Id))
                .EnsureAsync(entity => entity.Version == command.Version, Widget.Errors.ChangedConcurrently(command.Id, command.Version))
                .MapAsync(entity => entity.Rename(name)))
            .EnsureAsync(changed => repository.UpdateAsync(changed, command.Version, ct))  // checked again as it is written
            .RecordAsync("PLURAL_CODE.rename");

    public Task<Result> RemoveAsync(Guid id, int version, CancellationToken ct) =>
        repository.RemoveAsync(id, version, ct)  // not_found or changed_concurrently from the store
            .RecordAsync("PLURAL_CODE.remove");
#endif
}
