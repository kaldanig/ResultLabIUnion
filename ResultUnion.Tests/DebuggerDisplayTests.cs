using System.Diagnostics;
using System.Reflection;

namespace ResultUnion.Tests;

/// <summary>
/// The debugger summary must come from <c>ToString()</c>: the default one evaluates every property, and
/// <c>Error</c> / <c>ResultValue</c> throw on the wrong track.
/// </summary>
public class DebuggerDisplayTests
{
    [Theory]
    [InlineData(typeof(Result<>))]
    [InlineData(typeof(Result))]
    [InlineData(typeof(Error))]
    public void Type_UsesToStringForTheDebuggerSummary(Type type)
    {
        var attribute = type.GetCustomAttribute<DebuggerDisplayAttribute>();

        Assert.NotNull(attribute);
        Assert.Equal("{ToString(),nq}", attribute.Value);
    }

    [Fact]
    public void ToString_DoesNotThrowOnEitherTrack()
    {
        Assert.Equal("Success(1)", Result<int>.Success(1).ToString());
        Assert.Equal("Failure(result.default: The result was default-constructed and carries no error.)", default(Result<int>).ToString());
        Assert.Equal("Success", Result.Success().ToString());
        Assert.Equal("Failure(result.default: The result was default-constructed and carries no error.)", default(Result).ToString());
    }
}
