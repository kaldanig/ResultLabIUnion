namespace ResultUnion.Tests.Contracts;

/// <summary>Every public method of ResultUnion rejects a <see langword="null"/> argument when it is called.</summary>
public sealed class NullArgumentTests
{
    public static TheoryData<string, string> Cases => NullArgumentContract.Cases(typeof(Result).Assembly);

    [Theory]
    [MemberData(nameof(Cases))]
    public void A_null_argument_is_rejected_when_the_method_is_called(string method, string parameter) =>
        NullArgumentContract.Verify(typeof(Result).Assembly, method, parameter);

    [Fact]
    public void The_contract_covers_the_async_pipeline() =>
        Assert.True(Cases.Count() > 200, $"Only {Cases.Count()} cases were found; the method discovery is broken.");
}
