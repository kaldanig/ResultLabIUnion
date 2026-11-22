using System.Collections.Concurrent;

namespace ResultUnionApi.Products;

/// <summary>Where products are stored. A lookup returns <see langword="null"/>; the service turns that into an error.</summary>
public interface IProductRepository
{
    Task<Product?> FindAsync(Guid id, CancellationToken ct);

    Task<bool> NameExistsAsync(ProductName name, CancellationToken ct);

    Task AddAsync(Product product, CancellationToken ct);
}

/// <summary>Keeps products in memory; replace it with a database (EF Core, Dapper, …) and keep the interface.</summary>
public sealed class InMemoryProductRepository : IProductRepository
{
    private readonly ConcurrentDictionary<Guid, Product> _products = new();

    public Task<Product?> FindAsync(Guid id, CancellationToken ct) =>
        Task.FromResult(_products.GetValueOrDefault(id));

    public Task<bool> NameExistsAsync(ProductName name, CancellationToken ct) =>
        Task.FromResult(_products.Values.Any(p => string.Equals(p.Name.Value, name.Value, StringComparison.OrdinalIgnoreCase)));

    public Task AddAsync(Product product, CancellationToken ct)
    {
        _products[product.Id] = product;
        return Task.CompletedTask;
    }
}
