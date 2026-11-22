using Grpc.Reflection.V1;

namespace ResultUnionGrpc.Tests;

/// <summary>
/// What grpcurl and Postman ask the service: the service itself, and the status detail types, so that they show a
/// failure's ErrorInfo and BadRequest as JSON.
/// </summary>
public sealed class ReflectionTests(ServiceFactory factory) : IClassFixture<ServiceFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("resultuniongrpc.products.v1.ProductCatalog")]
    [InlineData("google.rpc.ErrorInfo")]
    [InlineData("google.rpc.BadRequest")]
    public async Task Reflection_describes(string symbol)
    {
        using var channel = factory.CreateChannel();
        using var call = new ServerReflection.ServerReflectionClient(channel).ServerReflectionInfo(cancellationToken: Ct);

        await call.RequestStream.WriteAsync(new ServerReflectionRequest { FileContainingSymbol = symbol }, Ct);
        await call.RequestStream.CompleteAsync();
        Assert.True(await call.ResponseStream.MoveNext(Ct));

        Assert.Equal(ServerReflectionResponse.MessageResponseOneofCase.FileDescriptorResponse, call.ResponseStream.Current.MessageResponseCase);
    }

    // Every service mapped with MapGrpcService is found, so a new one needs no change to the reflection setup.
    [Fact]
    public async Task Reflection_lists_the_mapped_services()
    {
        using var channel = factory.CreateChannel();
        using var call = new ServerReflection.ServerReflectionClient(channel).ServerReflectionInfo(cancellationToken: Ct);

        await call.RequestStream.WriteAsync(new ServerReflectionRequest { ListServices = "" }, Ct);
        await call.RequestStream.CompleteAsync();
        Assert.True(await call.ResponseStream.MoveNext(Ct));

        var names = call.ResponseStream.Current.ListServicesResponse.Service.Select(service => service.Name);
        Assert.Contains("resultuniongrpc.products.v1.ProductCatalog", names);
    }
}
