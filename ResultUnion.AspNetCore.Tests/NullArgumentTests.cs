using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using ResultUnion.AspNetCore;
using ResultUnion.Tests.Contracts;

namespace ResultUnion.AspNetCore.Tests;

/// <summary>Every public method of ResultUnion.AspNetCore rejects a <see langword="null"/> argument when it is called.</summary>
public sealed class NullArgumentTests
{
    private static readonly Assembly Package = typeof(ResultHttpExtensions).Assembly;

    // A typed success result for the TSuccess type parameter, which is constrained to IResult.
    private static Type? CloseType(Type parameter) =>
        parameter.GetGenericParameterConstraints().Contains(typeof(IResult)) ? typeof(Ok<int>) : null;

    private static object? Sample(Type type) =>
        type == typeof(Ok<int>) ? TypedResults.Ok(1)
        : type == typeof(IResult) ? Results.Ok()
        : null;

    public static TheoryData<string, string> Cases => NullArgumentContract.Cases(Package, CloseType, Sample);

    [Theory]
    [MemberData(nameof(Cases))]
    public void A_null_argument_is_rejected_when_the_method_is_called(string method, string parameter) =>
        NullArgumentContract.Verify(Package, method, parameter, CloseType, Sample);

    [Fact]
    public void The_contract_covers_the_http_extensions() =>
        Assert.True(Cases.Count() >= 15, $"Only {Cases.Count()} cases were found; the method discovery is broken.");
}
