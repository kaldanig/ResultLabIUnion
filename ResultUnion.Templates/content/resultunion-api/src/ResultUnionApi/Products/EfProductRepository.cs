using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ResultUnionApi.Data;

namespace ResultUnionApi.Products;

/// <summary>
/// Products in SQLite, through EF Core. A taken name is checked first, for a clear answer in the common case, and
/// enforced by the unique index, for two requests that check at the same time.
/// </summary>
public sealed class EfProductRepository(AppDbContext db) : IProductRepository
{
    public Task<Product?> FindAsync(Guid id, CancellationToken ct) =>
        db.Products.FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<Result> AddAsync(Product product, CancellationToken ct)
    {
        if (await db.Products.AnyAsync(p => p.Name == product.Name, ct))
            return Product.Errors.NameTaken(product.Name);

        db.Products.Add(product);
        try
        {
            await db.SaveChangesAsync(ct);
            return Result.Success();
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqliteException { SqliteExtendedErrorCode: 2067 })   // UNIQUE constraint failed
        {
            db.Entry(product).State = EntityState.Detached;
            return Product.Errors.NameTaken(product.Name);
        }
    }
}
