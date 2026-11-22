using Microsoft.EntityFrameworkCore;
using ResultUnionApi.Products;

namespace ResultUnionApi.Data;

/// <summary>
/// The database. Value objects are stored as their value and read back through their <c>Create</c>: a value that no
/// longer passes is a bug in the data, so it throws instead of becoming a result.
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var product = modelBuilder.Entity<Product>();
        product.HasKey(p => p.Id);
        product.Property(p => p.Name)
            .HasConversion(name => name.Value, value => FromDatabase(ProductName.Create(value)))
            .HasMaxLength(ProductName.MaxLength)
            .UseCollation("NOCASE");                                            // "Coffee" and "coffee" are one name
        product.HasIndex(p => p.Name).IsUnique();
        product.Property(p => p.Price)
            .HasConversion(price => price.Amount, amount => FromDatabase(Price.Create(amount)));
    }

    private static T FromDatabase<T>(Result<T> value) where T : notnull =>
        value.Match(valid => valid, error => throw new InvalidOperationException($"Stored data is invalid: {error}"));
}
