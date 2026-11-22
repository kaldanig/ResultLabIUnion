using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ResultUnion;

namespace TargetNamespace.Gizmos;

/// <summary>
/// ENTITY_PLURAL_WORDS in SQLite, through EF Core, in the application's own <typeparamref name="TContext"/> (whose
/// model applies <see cref="WidgetConfiguration"/>). A taken name is checked first, for a clear answer in the common
/// case, and enforced by the unique index, for two requests that check at the same time.
/// </summary>
public sealed class EfWidgetRepository<TContext>(TContext db) : IWidgetRepository
    where TContext : DbContext
{
    public Task<Widget?> FindAsync(Guid id, CancellationToken ct) =>
        db.Set<Widget>().FirstOrDefaultAsync(e => e.Id == id, ct);

    public async Task<Result> AddAsync(Widget entity, CancellationToken ct)
    {
        if (await db.Set<Widget>().AnyAsync(e => e.Name == entity.Name, ct))
            return Widget.Errors.NameTaken(entity.Name);

        db.Set<Widget>().Add(entity);
        try
        {
            await db.SaveChangesAsync(ct);
            return Result.Success();
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqliteException { SqliteExtendedErrorCode: 2067 })   // UNIQUE constraint failed
        {
            db.Entry(entity).State = EntityState.Detached;
            return Widget.Errors.NameTaken(entity.Name);
        }
    }
#if (Crud)

    // One UPDATE ... WHERE Id = @id AND Version = @expected: a row changed in between is not overwritten, and the
    // unique index answers for a taken name (nothing is tracked, so there is nothing to undo after it).
    public async Task<Result> UpdateAsync(Widget changed, int expectedVersion, CancellationToken ct)
    {
        try
        {
            var rows = await db.Set<Widget>()
                .Where(e => e.Id == changed.Id && e.Version == expectedVersion)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(e => e.Name, changed.Name)
                    .SetProperty(e => e.Version, changed.Version), ct);
            if (rows == 1)
                return Result.Success();

            return await MissingOrChangedAsync(changed.Id, expectedVersion, ct);
        }
        catch (SqliteException ex) when (ex.SqliteExtendedErrorCode == 2067)   // UNIQUE constraint failed
        {
            return Widget.Errors.NameTaken(changed.Name);
        }
    }

    public async Task<Result> RemoveAsync(Guid id, int expectedVersion, CancellationToken ct)
    {
        var rows = await db.Set<Widget>()
            .Where(e => e.Id == id && e.Version == expectedVersion)
            .ExecuteDeleteAsync(ct);
        if (rows == 1)
            return Result.Success();

        return await MissingOrChangedAsync(id, expectedVersion, ct);
    }

    // No row was written: either there is none, or its version is another.
    private async Task<Error> MissingOrChangedAsync(Guid id, int expectedVersion, CancellationToken ct) =>
        await db.Set<Widget>().AnyAsync(e => e.Id == id, ct)
            ? Widget.Errors.ChangedConcurrently(id, expectedVersion)
            : Widget.Errors.NotFound(id);
#endif
}
