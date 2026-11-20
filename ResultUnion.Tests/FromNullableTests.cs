using static ResultUnion.Tests.Async.EagerAssert;

namespace ResultUnion.Tests;

/// <summary><c>Result.FromNullable</c> and <c>Task&lt;T?&gt;.ToResultAsync</c>: "not found as null" turned into a result.</summary>
public class FromNullableTests
{
    private static readonly Error NotFound = Error.NotFound("Item was not found.");

    private static async Task<T> Later<T>(T value)
    {
        await Task.Yield();
        return value;
    }

    // Reference types

    [Fact]
    public void FromNullable_Reference_ValueOrError()
    {
        Assert.Equal("ann", Result.FromNullable<string>("ann", NotFound).ResultValue);
        Assert.Same(NotFound, Result.FromNullable<string>(null, NotFound).Error);
    }

    // Value types

    [Fact]
    public void FromNullable_Struct_ValueOrError()
    {
        int? present = 0;
        int? missing = null;

        Result<int> fromPresent = Result.FromNullable(present, NotFound);   // Result<int>, not Result<int?>
        Assert.Equal(0, fromPresent.ResultValue);
        Assert.Same(NotFound, Result.FromNullable(missing, NotFound).Error);
    }

    [Fact]
    public void FromNullable_NullError_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => Result.FromNullable("ann", null!));
        Assert.Throws<ArgumentNullException>(() => Result.FromNullable((int?)1, null!));
    }

    [Fact]
    public void FromNullable_DictionaryLookup()
    {
        var users = new Dictionary<int, string> { [1] = "ann" };

        Assert.Equal("ann", Result.FromNullable(users.GetValueOrDefault(1), NotFound).ResultValue);
        Assert.Same(NotFound, Result.FromNullable(users.GetValueOrDefault(2), NotFound).Error);
    }

    // ToResultAsync

    [Fact]
    public async Task ToResultAsync_Reference_ValueOrError()
    {
        Result<string> found = await Later<string?>("ann").ToResultAsync(NotFound);
        Result<string> missing = await Later<string?>(null).ToResultAsync(NotFound);

        Assert.Equal("ann", found.ResultValue);
        Assert.Same(NotFound, missing.Error);
    }

    [Fact]
    public async Task ToResultAsync_Struct_ValueOrError()
    {
        Result<int> found = await Later<int?>(7).ToResultAsync(NotFound);
        Result<int> missing = await Later<int?>(null).ToResultAsync(NotFound);

        Assert.Equal(7, found.ResultValue);
        Assert.Same(NotFound, missing.Error);
    }

    [Fact]
    public async Task ToResultAsync_FaultedLookup_IsNotCaught()
    {
        var faulted = Task.FromException<string?>(new InvalidOperationException("database down"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => faulted.ToResultAsync(NotFound));
        Assert.Equal("database down", exception.Message);
    }

    [Fact]
    public void ToResultAsync_NullArguments_ThrowWhenCalled()
    {
        ThrowsEagerly(() => _ = ((Task<string?>)null!).ToResultAsync(NotFound));
        ThrowsEagerly(() => _ = ((Task<int?>)null!).ToResultAsync(NotFound));
        ThrowsEagerly(() => _ = Later<string?>("ann").ToResultAsync(null!));
    }

    // Typical use: a nullable lookup at the start of a pipeline.

    [Theory]
    [InlineData(1, "ok:ANN")]
    [InlineData(2, "error:resource.not_found")]
    public async Task ToResultAsync_StartsAPipeline(int id, string expected)
    {
        Task<string?> FirstOrDefaultAsync(int key) => Later(key == 1 ? "ann" : null);

        var outcome = await FirstOrDefaultAsync(id)
            .ToResultAsync(NotFound)
            .MapAsync(name => name.ToUpperInvariant())
            .MatchAsync(name => $"ok:{name}", error => $"error:{error.Code}");

        Assert.Equal(expected, outcome);
    }
}
