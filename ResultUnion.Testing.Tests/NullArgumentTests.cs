using System.Reflection;
using ResultUnion.Testing;
using ResultUnion.Tests.Contracts;

namespace ResultUnion.Testing.Tests;

/// <summary>Every public method of ResultUnion.Testing rejects a <see langword="null"/> argument when it is called.</summary>
public sealed class NullArgumentTests
{
    private static readonly Assembly Package = typeof(ResultAssertions).Assembly;

    // A failed result, so that the assertions that expect a failure get past the result and reach their arguments.
    private static object? Sample(Type type) =>
        type == typeof(Result) ? Result.Failure(Error.Validation("Invalid."))
        : type == typeof(Result<int>) ? Result<int>.Failure(Error.Validation("Invalid."))
        : type == typeof(IEnumerable<(string?, string)>) ? Array.Empty<(string?, string)>()
        : null;

    public static TheoryData<string, string> Cases => NullArgumentContract.Cases(Package, sample: Sample);

    [Theory]
    [MemberData(nameof(Cases))]
    public void A_null_argument_is_rejected_when_the_method_is_called(string method, string parameter) =>
        NullArgumentContract.Verify(Package, method, parameter, sample: Sample);

    [Fact]
    public void The_contract_covers_the_assertions() =>
        Assert.True(Cases.Count() >= 8, $"Only {Cases.Count()} cases were found; the method discovery is broken.");
}
