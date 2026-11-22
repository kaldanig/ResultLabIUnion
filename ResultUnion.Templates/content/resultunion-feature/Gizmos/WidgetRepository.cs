using ResultUnion;

namespace TargetNamespace.Gizmos;

/// <summary>
/// Where ENTITY_PLURAL_WORDS are stored. A lookup returns <see langword="null"/>, which the service turns into an
/// error; adding one whose name is taken is the store's answer, a failure with <see cref="Widget.Errors.NameTaken"/>.
#if (Crud)
/// A change or a removal names the version it was based on, and fails with
/// <see cref="Widget.Errors.ChangedConcurrently"/> when the stored one is another: the check and the write are one step.
#endif
/// </summary>
public interface IWidgetRepository
{
    Task<Widget?> FindAsync(Guid id, CancellationToken ct);

    Task<Result> AddAsync(Widget entity, CancellationToken ct);
#if (Crud)

    Task<Result> UpdateAsync(Widget changed, int expectedVersion, CancellationToken ct);

    Task<Result> RemoveAsync(Guid id, int expectedVersion, CancellationToken ct);
#endif
}

/// <summary>Keeps ENTITY_PLURAL_WORDS in memory, for a start or for tests; a database goes behind the same interface.</summary>
public sealed class InMemoryWidgetRepository : IWidgetRepository
{
    private readonly Dictionary<Guid, Widget> _entities = [];
    private readonly Lock _gate = new();

    public Task<Widget?> FindAsync(Guid id, CancellationToken ct)
    {
        lock (_gate)
            return Task.FromResult(_entities.GetValueOrDefault(id));
    }

    public Task<Result> AddAsync(Widget entity, CancellationToken ct)
    {
        lock (_gate)
        {
            if (IsTaken(entity))
                return Task.FromResult<Result>(Widget.Errors.NameTaken(entity.Name));

            _entities[entity.Id] = entity;
            return Task.FromResult(Result.Success());
        }
    }
#if (Crud)

    public Task<Result> UpdateAsync(Widget changed, int expectedVersion, CancellationToken ct)
    {
        lock (_gate)
        {
            if (!_entities.TryGetValue(changed.Id, out var stored))
                return Task.FromResult<Result>(Widget.Errors.NotFound(changed.Id));
            if (stored.Version != expectedVersion)
                return Task.FromResult<Result>(Widget.Errors.ChangedConcurrently(changed.Id, expectedVersion));
            if (IsTaken(changed))
                return Task.FromResult<Result>(Widget.Errors.NameTaken(changed.Name));

            _entities[changed.Id] = changed;
            return Task.FromResult(Result.Success());
        }
    }

    public Task<Result> RemoveAsync(Guid id, int expectedVersion, CancellationToken ct)
    {
        lock (_gate)
        {
            if (!_entities.TryGetValue(id, out var stored))
                return Task.FromResult<Result>(Widget.Errors.NotFound(id));
            if (stored.Version != expectedVersion)
                return Task.FromResult<Result>(Widget.Errors.ChangedConcurrently(id, expectedVersion));

            _entities.Remove(id);
            return Task.FromResult(Result.Success());
        }
    }
#endif

    // Another entity with the same name, ignoring case.
    private bool IsTaken(Widget entity) =>
        _entities.Values.Any(e => e.Id != entity.Id && string.Equals(e.Name.Value, entity.Name.Value, StringComparison.OrdinalIgnoreCase));
}
