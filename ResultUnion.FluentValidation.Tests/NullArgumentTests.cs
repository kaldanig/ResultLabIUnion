using System.Reflection;
using FluentValidation;
using FluentValidation.Results;
using ResultUnion.FluentValidation;
using ResultUnion.Tests.Contracts;

namespace ResultUnion.FluentValidation.Tests;

/// <summary>Every public method of ResultUnion.FluentValidation rejects a <see langword="null"/> argument when it is called.</summary>
public sealed class NullArgumentTests
{
    private static readonly Assembly Package = typeof(ValidationResultExtensions).Assembly;

    private static object? Sample(Type type) =>
        type == typeof(ValidationResult) ? new ValidationResult()
        : type == typeof(IValidator<int>) ? new InlineValidator<int>()
        : null;

    public static TheoryData<string, string> Cases => NullArgumentContract.Cases(Package, sample: Sample);

    [Theory]
    [MemberData(nameof(Cases))]
    public void A_null_argument_is_rejected_when_the_method_is_called(string method, string parameter) =>
        NullArgumentContract.Verify(Package, method, parameter, sample: Sample);

    [Fact]
    public void The_contract_covers_the_extensions() =>
        Assert.True(Cases.Count() >= 4, $"Only {Cases.Count()} cases were found; the method discovery is broken.");
}
