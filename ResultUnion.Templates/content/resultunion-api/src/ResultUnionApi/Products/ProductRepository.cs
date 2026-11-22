namespace ResultUnionApi.Products;

/// <summary>
/// Where products are stored. A lookup returns <see langword="null"/>, which the service turns into an error; adding a
/// product whose name is taken is the store's answer, a failure with <see cref="Product.Errors.NameTaken"/>.
/// </summary>
public interface IProductRepository
{
    Task<Product?> FindAsync(Guid id, CancellationToken ct);

    Task<Result> AddAsync(Product product, CancellationToken ct);
}

/// <summary>Keeps products in memory, for a start or for tests; a database goes behind the same interface.</summary>
public sealed class InMemoryProductRepository : IProductRepository
{
    private readonly Dictionary<Guid, Product> _products = [];
    private readonly Lock _gate = new();

    public Task<Product?> FindAsync(Guid id, CancellationToken ct)
    {
        lock (_gate)
            return Task.FromResult(_products.GetValueOrDefault(id));
    }

    public Task<Result> AddAsync(Product product, CancellationToken ct)
    {
        lock (_gate)
        {
            if (_products.Values.Any(p => string.Equals(p.Name.Value, product.Name.Value, StringComparison.OrdinalIgnoreCase)))
                return Task.FromResult<Result>(Product.Errors.NameTaken(product.Name));

            _products[product.Id] = product;
            return Task.FromResult(Result.Success());
        }
    }
}
