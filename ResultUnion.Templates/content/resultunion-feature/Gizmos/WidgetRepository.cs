using ResultUnion;

namespace TargetNamespace.Gizmos;

/// <summary>
/// Where ENTITY_PLURAL_WORDS are stored. A lookup returns <see langword="null"/>, which the service turns into an
/// error; adding one whose name is taken is the store's answer, a failure with <see cref="Widget.Errors.NameTaken"/>.
/// </summary>
public interface IWidgetRepository
{
    Task<Widget?> FindAsync(Guid id, CancellationToken ct);

    Task<Result> AddAsync(Widget entity, CancellationToken ct);
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
            if (_entities.Values.Any(e => string.Equals(e.Name.Value, entity.Name.Value, StringComparison.OrdinalIgnoreCase)))
                return Task.FromResult<Result>(Widget.Errors.NameTaken(entity.Name));

            _entities[entity.Id] = entity;
            return Task.FromResult(Result.Success());
        }
    }
}
