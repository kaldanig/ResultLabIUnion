using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ResultUnion;

namespace TargetNamespace.Gizmos;

/// <summary>
/// How a ENTITY_WORDS is stored; the DbContext applies it in <c>OnModelCreating</c>
/// (<c>modelBuilder.ApplyConfiguration(new WidgetConfiguration())</c>). The name is stored as its value and read back
/// through <see cref="WidgetName.Create"/>: a value that no longer passes is a bug in the data, so it throws.
/// </summary>
public sealed class WidgetConfiguration : IEntityTypeConfiguration<Widget>
{
    public void Configure(EntityTypeBuilder<Widget> entity)
    {
        entity.ToTable("Gizmos");
        entity.HasKey(e => e.Id);
        entity.Property(e => e.Name)
            .HasConversion(name => name.Value, value => FromDatabase(WidgetName.Create(value)))
            .HasMaxLength(WidgetName.MaxLength)
            .UseCollation("NOCASE");  // names that differ only in case are one name
        entity.HasIndex(e => e.Name).IsUnique();
#if (Crud)
        entity.Property(e => e.Version);  // read-only, so mapped by name; the repository compares it as it writes
#endif
    }

    private static T FromDatabase<T>(Result<T> value) where T : notnull =>
        value.Match(valid => valid, error => throw new InvalidOperationException($"Stored data is invalid: {error}"));
}
