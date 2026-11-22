using ResultUnion;

namespace TargetNamespace.Gizmos;

/// <summary>The raw input of "create a ENTITY_WORDS", from any host (HTTP here, a message or a test elsewhere).</summary>
public sealed record CreateWidget(string? Name);

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
}
