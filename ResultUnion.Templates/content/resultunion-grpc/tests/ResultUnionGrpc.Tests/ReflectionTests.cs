using Grpc.Net.Client;
using Grpc.Reflection.V1;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ResultUnionGrpc.Tests;

/// <summary>
/// What grpcurl and Postman ask the service: the service itself, and the status detail types, so that they show a
/// failure's ErrorInfo and BadRequest as JSON.
/// </summary>
public sealed class ReflectionTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("resultuniongrpc.products.v1.ProductCatalog")]
    [InlineData("google.rpc.ErrorInfo")]
    [InlineData("google.rpc.BadRequest")]
    public async Task Reflection_describes(string symbol)
    {
        using var channel = GrpcChannel.ForAddress(factory.Server.BaseAddress, new GrpcChannelOptions { HttpHandler = factory.Server.CreateHandler() });
        using var call = new ServerReflection.ServerReflectionClient(channel).ServerReflectionInfo(cancellationToken: Ct);

        await call.RequestStream.WriteAsync(new ServerReflectionRequest { FileContainingSymbol = symbol }, Ct);
        await call.RequestStream.CompleteAsync();
        Assert.True(await call.ResponseStream.MoveNext(Ct));

        Assert.Equal(ServerReflectionResponse.MessageResponseOneofCase.FileDescriptorResponse, call.ResponseStream.Current.MessageResponseCase);
    }
}
