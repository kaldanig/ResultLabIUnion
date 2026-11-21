using Microsoft.EntityFrameworkCore;
using ResultUnion;

namespace ResultUnion.Articles;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<User> Users => Set<User>();

    private static Email FromDb(string v) => Email.Create(v).Match(e => e, _ => throw new InvalidOperationException());

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>().OwnsMany(o => o.Lines);
        modelBuilder.Entity<User>().Property(u => u.Email).HasConversion(e => e.Value, v => FromDb(v));
    }
}

public sealed class OrderRepository(AppDbContext db)
{
    // ---- verbatim from ef-core.md ----
    public Task<Result<Order>> GetAsync(Guid id, CancellationToken ct) =>
        db.Orders.FindAsync([id], ct).ToResultAsync(Order.Errors.NotFound(id));

    public Task<Result<Order>> GetByNumberAsync(string number, CancellationToken ct) =>
        db.Orders.Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Number == number, ct)
            .ToResultAsync(Order.Errors.NumberNotFound(number));

    public Task<Result> SaveAsync(Order order, CancellationToken ct) =>
        Result.TryAsync(
            async () => { await db.SaveChangesAsync(ct); },
            (DbUpdateConcurrencyException _) => Order.Errors.ChangedConcurrently(order.Id));
}

public sealed class EfUserRepository(AppDbContext db)
{
    // ---- verbatim from ef-core.md ----
    public async Task<Result> AddAsync(User user, CancellationToken ct)
    {
        if (await db.Users.AnyAsync(u => u.Email == user.Email, ct))
            return User.Errors.EmailTaken(user.Email);

        db.Users.Add(user);
        try
        {
            await db.SaveChangesAsync(ct);
            return Result.Success();
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            db.Entry(user).State = EntityState.Detached;
            return User.Errors.EmailTaken(user.Email);
        }
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is Microsoft.Data.Sqlite.SqliteException { SqliteExtendedErrorCode: 2067 };
}

public sealed class OrderService(OrderRepository orders)
{
    // ---- verbatim from ef-core.md ----
    public Task<Result<Order>> CancelAsync(Guid orderId, string? reason, CancellationToken ct) =>
        CancellationReason.Create(reason)
            .MapError(e => e.WithField("reason"))
            .BindAsync(validReason => orders.GetAsync(orderId, ct)
                .BindAsync(order => order.Cancel(validReason)))
            .EnsureAsync(order => orders.SaveAsync(order, ct));
}
