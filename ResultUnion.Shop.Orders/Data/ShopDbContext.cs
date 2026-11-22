using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using ResultUnion.Shop.Orders.Customers;
using ResultUnion.Shop.Orders.Orders;

namespace ResultUnion.Shop.Orders.Data;

/// <summary>
/// Customers and orders in SQLite. Value objects are stored as their value and read back through their factory; a
/// stored value that no longer passes is a bug in the data, so it throws. Each entity's <c>Version</c> is a concurrency
/// token, so an update from a stale version changes no row and EF Core throws <see cref="DbUpdateConcurrencyException"/>.
/// </summary>
public sealed class ShopDbContext(DbContextOptions<ShopDbContext> options) : DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var customer = modelBuilder.Entity<Customer>();
        customer.HasKey(c => c.Id);
        customer.Property(c => c.Name).HasConversion(name => name.Value, value => Stored(CustomerName.Create(value))).HasMaxLength(CustomerName.MaxLength);
        customer.Property(c => c.Email).HasConversion(email => email.Value, value => Stored(CustomerEmail.Create(value)));
        customer.HasIndex(c => c.Email).IsUnique();
        customer.Property(c => c.Version).IsConcurrencyToken();

        var order = modelBuilder.Entity<Order>();
        order.HasKey(o => o.Id);
        order.Property(o => o.Status).HasConversion<string>();
        order.Property(o => o.Version).IsConcurrencyToken();
        order.Property(o => o.Lines)                                   // the lines are part of the order: one JSON column
            .HasConversion(
                lines => JsonSerializer.Serialize(lines, JsonSerializerOptions.Web),
                json => JsonSerializer.Deserialize<List<OrderLine>>(json, JsonSerializerOptions.Web) ?? new List<OrderLine>(),
                new ValueComparer<IReadOnlyList<OrderLine>>((a, b) => a!.SequenceEqual(b!), lines => lines.Aggregate(0, (hash, line) => HashCode.Combine(hash, line))));
    }

    private static T Stored<T>(Result<T> value) where T : notnull =>
        value.Match(valid => valid, error => throw new InvalidOperationException($"Stored data is invalid: {error}"));
}
