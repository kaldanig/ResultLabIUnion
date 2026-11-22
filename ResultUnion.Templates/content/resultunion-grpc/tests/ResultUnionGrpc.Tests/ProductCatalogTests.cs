using Grpc.Core;
using Grpc.Net.Client;
using ResultUnion.Grpc;
using ResultUnionGrpc.Products.V1;

namespace ResultUnionGrpc.Tests;

/// <summary>
/// The service in memory, called through the generated gRPC client: what a client receives, failures included. A .NET
/// client turns a failed call back into an <see cref="Error"/> with <c>ToError()</c>, with the same codes and fields.
/// </summary>
public sealed class ProductCatalogTests : IClassFixture<ServiceFactory>, IDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly GrpcChannel _channel;
    private readonly ProductCatalog.ProductCatalogClient _client;

    public ProductCatalogTests(ServiceFactory factory)
    {
        _channel = factory.CreateChannel();
        _client = new ProductCatalog.ProductCatalogClient(_channel);
    }

    public void Dispose() => _channel.Dispose();

    // Only an RpcException becomes an error; anything else is a bug and stays an exception.
    private Task<Result<ProductReply>> CreateAsync(string name, double? price)
    {
        var request = new CreateProductRequest { Name = name };
        if (price is { } value)
            request.Price = value;

        return Result.TryAsync(() => _client.CreateProductAsync(request, cancellationToken: Ct).ResponseAsync, (RpcException ex) => ex.ToError());
    }

    [Fact]
    public async Task A_valid_product_is_created_and_can_be_read_back()
    {
        var created = await CreateAsync($"Coffee {Guid.NewGuid()}", 4.5).ShouldSucceedAsync();

        var found = await _client.GetProductAsync(new GetProductRequest { Id = created.Id }, cancellationToken: Ct);

        Assert.Equal(created, found);
    }

    [Fact]
    public async Task Invalid_input_is_INVALID_ARGUMENT_with_a_code_per_field()
    {
        var exception = await Assert.ThrowsAsync<RpcException>(() => _client.CreateProductAsync(new CreateProductRequest { Name = "" }, cancellationToken: Ct).ResponseAsync);
        Assert.Equal(StatusCode.InvalidArgument, exception.StatusCode);

        // The status carries what the error had: its code, and every invalid field with its own code.
        var error = exception.ToError();
        Assert.Equal("validation.failed", error.Code);
        error.ShouldHaveFieldErrors(("name", "product_name.required"), ("price", "price.required"));
    }

    [Fact]
    public async Task A_taken_name_is_FAILED_PRECONDITION_with_its_code()
    {
        var name = $"Tea {Guid.NewGuid()}";
        await CreateAsync(name, 3).ShouldSucceedAsync();

        var error = await CreateAsync(name, 3).ShouldFailAsync("product.name_taken");

        error.ShouldHaveType(ErrorType.Conflict);
    }

    [Fact]
    public async Task An_unknown_product_is_NOT_FOUND()
    {
        var exception = await Assert.ThrowsAsync<RpcException>(() => _client.GetProductAsync(new GetProductRequest { Id = Guid.NewGuid().ToString() }, cancellationToken: Ct).ResponseAsync);

        Assert.Equal(StatusCode.NotFound, exception.StatusCode);
        Assert.Equal("product.not_found", exception.ToError().Code);
    }

    [Fact]
    public async Task An_id_that_is_not_a_guid_is_INVALID_ARGUMENT_on_the_id_field()
    {
        var exception = await Assert.ThrowsAsync<RpcException>(() => _client.GetProductAsync(new GetProductRequest { Id = "42" }, cancellationToken: Ct).ResponseAsync);

        Assert.Equal(StatusCode.InvalidArgument, exception.StatusCode);
        var error = exception.ToError();
        Assert.Equal("product.invalid_id", error.Code);
        Assert.Equal("id", error.Field);
    }
}
