using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ResultUnion.Shop.Orders.Data;

namespace ResultUnion.Shop.Orders.Customers;

public sealed record CreateCustomer(string? Name, string? Email);

public sealed record RenameCustomer(Guid Id, string? Name, int Version);

/// <summary>The customers' use cases: create, find, rename and delete, the last two from the version a client read.</summary>
public sealed class CustomerService(ShopDbContext db)
{
    public Task<Result<Customer>> CreateAsync(CreateCustomer command, CancellationToken ct) =>
        Result.CombineAll(
                CustomerName.Create(command.Name).MapError(e => e.WithField("name")),
                CustomerEmail.Create(command.Email).MapError(e => e.WithField("email")))
            .Map(Customer.Create)
            .EnsureAsync(customer => AddAsync(customer, ct))
            .RecordAsync("customers.create");

    public Task<Result<Customer>> FindAsync(Guid id, CancellationToken ct) =>
        db.Customers.FindAsync([id], ct).ToResultAsync(Customer.Errors.NotFound(id));

    public Task<Result<Customer>> RenameAsync(RenameCustomer command, CancellationToken ct) =>
        CustomerName.Create(command.Name)
            .MapError(e => e.WithField("name"))
            .BindAsync(name => FindAsync(command.Id, ct)
                .TapAsync(customer =>
                {
                    ExpectVersion(customer, command.Version);    // the database compares it as it writes
                    customer.Rename(name);
                }))
            .EnsureAsync(customer => SaveAsync(customer, command.Version, ct))
            .RecordAsync("customers.rename");

    public Task<Result> DeleteAsync(Guid id, int version, CancellationToken ct) =>
        FindAsync(id, ct)
            .BindAsync(customer =>
            {
                ExpectVersion(customer, version);
                db.Customers.Remove(customer);
                return SaveAsync(customer, version, ct);
            })
            .RecordAsync("customers.delete");

    // A unique index enforces the email, so two requests that add the same one at once cannot both succeed.
    private async Task<Result> AddAsync(Customer customer, CancellationToken ct)
    {
        db.Customers.Add(customer);
        try
        {
            await db.SaveChangesAsync(ct);
            return Result.Success();
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqliteException { SqliteExtendedErrorCode: 2067 })   // UNIQUE constraint failed
        {
            db.Entry(customer).State = EntityState.Detached;
            return Customer.Errors.EmailTaken(customer.Email);
        }
    }

    private Task<Result> SaveAsync(Customer customer, int version, CancellationToken ct) =>
        Result.TryAsync(
            async () => { await db.SaveChangesAsync(ct); },
            (DbUpdateConcurrencyException _) => Customer.Errors.ChangedConcurrently(customer.Id, version));

    // EF Core puts the original version into the UPDATE's WHERE clause; the one the client read is the original.
    private void ExpectVersion(Customer customer, int version) =>
        db.Entry(customer).Property(c => c.Version).OriginalValue = version;
}
