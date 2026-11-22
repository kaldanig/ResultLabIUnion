using Grpc.Core;
using ResultUnion.Grpc;
using ResultUnionGrpc.Products.V1;

namespace ResultUnionGrpc.Products;

/// <summary>
/// The gRPC host of the use cases. Each method maps the request to a command, runs the use case, and ends with
/// <c>GetValueOrThrowRpcAsync</c>: the reply on success, or an <see cref="RpcException"/> whose status is chosen by the
/// error's type (Validation is INVALID_ARGUMENT, NotFound NOT_FOUND, Conflict FAILED_PRECONDITION), with an ErrorInfo
/// that carries the code and arguments and, for validation, a BadRequest with every invalid field.
/// </summary>
public sealed class ProductCatalogService(ProductService service) : ProductCatalog.ProductCatalogBase
{
    /// <summary>ErrorInfo.domain: who defines the codes. Clients use it to tell your codes from another service's.</summary>
    internal const string Domain = "resultuniongrpc";

    public override Task<ProductReply> CreateProduct(CreateProductRequest request, ServerCallContext context) =>
        service.CreateAsync(new CreateProduct(request.Name, request.HasPrice ? (decimal)request.Price : null), context.CancellationToken)
            .MapAsync(ToReply)
            .GetValueOrThrowRpcAsync(Domain);

    public override Task<ProductReply> GetProduct(GetProductRequest request, ServerCallContext context) =>
        service.FindAsync(request.Id, context.CancellationToken)
            .MapAsync(ToReply)
            .GetValueOrThrowRpcAsync(Domain);

    /// <summary>What a client receives. A <c>Result</c> is never sent; its value is mapped to the reply message.</summary>
    private static ProductReply ToReply(Product product) => new()
    {
        Id = product.Id.ToString(),
        Name = product.Name.Value,
        Price = (double)product.Price.Amount,
    };
}
