using System.Reflection;
using Grpc.Core;
using ResultUnion.Grpc;
using ResultUnion.Tests.Contracts;

namespace ResultUnion.Grpc.Tests;

/// <summary>Every public method of ResultUnion.Grpc rejects a <see langword="null"/> argument when it is called.</summary>
public sealed class NullArgumentTests
{
    private static readonly Assembly Package = typeof(ResultRpcExtensions).Assembly;

    private static object? Sample(Type type) =>
        type == typeof(RpcException) ? new RpcException(new Status(StatusCode.NotFound, "Missing.")) : null;

    public static TheoryData<string, string> Cases => NullArgumentContract.Cases(Package, sample: Sample);

    [Theory]
    [MemberData(nameof(Cases))]
    public void A_null_argument_is_rejected_when_the_method_is_called(string method, string parameter) =>
        NullArgumentContract.Verify(Package, method, parameter, sample: Sample);

    [Fact]
    public void The_contract_covers_the_grpc_extensions() =>
        Assert.True(Cases.Count() >= 8, $"Only {Cases.Count()} cases were found; the method discovery is broken.");
}
