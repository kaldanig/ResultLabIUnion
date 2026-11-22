using ResultUnion.AspNetCore;

namespace ResultUnionApi.Products;

public sealed record CreateProductRequest(string? Name, decimal? Price);

/// <summary>What a client receives. A <c>Result</c> is never serialized; its value is mapped to this.</summary>
public sealed record ProductResponse(Guid Id, string Name, decimal Price)
{
    public static ProductResponse From(Product product) => new(product.Id, product.Name.Value, product.Price.Amount);
}

public static class ProductEndpoints
{
    /// <summary>
    /// A success becomes the typed response, a failure an RFC 9457 problem with the error's <c>code</c>, its
    /// arguments and, for validation, every invalid field. ProducesErrors documents the possible problems in OpenAPI.
    /// </summary>
    public static IEndpointRouteBuilder MapProducts(this IEndpointRouteBuilder app)
    {
        var products = app.MapGroup("/products").WithTags("Products");

        products.MapPost("/", (CreateProductRequest request, ProductService service, CancellationToken ct) =>
                service.CreateAsync(new CreateProduct(request.Name, request.Price), ct)
                    .MapAsync(ProductResponse.From)
                    .ToHttpResultAsync(product => TypedResults.Created($"/products/{product.Id}", product)))
            .ProducesErrors(ErrorType.Validation, ErrorType.Conflict);

        products.MapGet("/{id:guid}", (Guid id, ProductService service, CancellationToken ct) =>
                service.FindAsync(id, ct)
                    .MapAsync(ProductResponse.From)
                    .ToHttpResultAsync())
            .ProducesErrors(ErrorType.NotFound);

        return app;
    }
}
