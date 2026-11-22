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
}
