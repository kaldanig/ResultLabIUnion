namespace ResultUnionGrpc.Products;

/// <summary>The raw input of "create a product", from any host (gRPC here, a message or a test elsewhere).</summary>
public sealed record CreateProduct(string? Name, decimal? Price);

/// <summary>The use cases. Each one is a single pipeline that runs while every step succeeds.</summary>
public sealed class ProductService(IProductRepository products)
{
    public Task<Result<Product>> CreateAsync(CreateProduct command, CancellationToken ct) =>
        Result.CombineAll(                                                      // every invalid field at once
                ProductName.Create(command.Name).MapError(e => e.WithField("name")),
                Price.Create(command.Price).MapError(e => e.WithField("price")))
            .Map(Product.Create)                                                // (name, price) => Product
            .EnsureAsync(product => products.AddAsync(product, ct))             // a taken name fails here
            .RecordAsync("products.create");                                    // failures counted by code

    public Task<Result<Product>> FindAsync(string? id, CancellationToken ct) =>
        Product.ParseId(id)
            .MapError(e => e.WithField("id"))
            .BindAsync(productId => products.FindAsync(productId, ct)
                .ToResultAsync(Product.Errors.NotFound(productId)))             // null becomes the catalog error
            .RecordAsync("products.find");
}
