using System.Collections;
using static ResultUnion.Tests.Async.EagerAssert;

namespace ResultUnion.Tests;

/// <summary><c>CombineAll</c> for sequences, <c>CombineAllAsync</c>, and <see cref="Error.WithFieldPrefix(string)"/>.</summary>
public class CollectionTests
{
    private static readonly Error QuantityInvalid = Error.Validation("Quantity must be positive.").WithField("quantity");
    private static readonly Error ProductMissing = Error.Validation("Product is required.").WithField("product");

    private static Result<int> Positive(int value) => value > 0 ? value : QuantityInvalid;

    // WithFieldPrefix

    [Fact]
    public void WithFieldPrefix_PrependsToTheField()
    {
        Assert.Equal("lines[2].quantity", QuantityInvalid.WithFieldPrefix("lines[2]").Field);
        Assert.Equal("lines[2]", Error.NotFound("x").WithFieldPrefix("lines[2]").Field);
        Assert.Equal("quantity", QuantityInvalid.Field);                       // the original is unchanged
    }

    [Fact]
    public void WithFieldPrefix_AppliesToEveryDetail()
    {
        var group = Error.Validation([ProductMissing, QuantityInvalid]).WithFieldPrefix("lines[0]");

        Assert.Equal(["lines[0].product", "lines[0].quantity"], group.Details.Select(d => d.Field));
        Assert.Equal("lines[0]", group.Field);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void WithFieldPrefix_BlankPrefix_Throws(string? prefix)
    {
        Assert.ThrowsAny<ArgumentException>(() => QuantityInvalid.WithFieldPrefix(prefix!));
    }

    // Flatten

    [Fact]
    public void Flatten_SingleError_IsItself()
    {
        Assert.Equal([QuantityInvalid], QuantityInvalid.Flatten());
    }

    [Fact]
    public void Flatten_NestedGroups_ReturnsInnermostErrorsInOrder()
    {
        var line0 = Error.Validation([ProductMissing, QuantityInvalid]).WithFieldPrefix("lines[0]");
        var line2 = QuantityInvalid.WithFieldPrefix("lines[2]");

        var order = Error.Validation([line0, line2]);

        Assert.Equal(["lines[0].product", "lines[0].quantity", "lines[2].quantity"], order.Flatten().Select(e => e.Field));
    }

    // CombineAll over Result<T>

    [Fact]
    public void Sequence_Empty_IsSuccessWithEmptyList()
    {
        Assert.Empty(Array.Empty<Result<int>>().CombineAll().ResultValue);
    }

    [Fact]
    public void Sequence_AllSucceed_KeepsValuesInOrder()
    {
        Result<int>[] results = [3, 1, 2];

        Assert.Equal([3, 1, 2], results.CombineAll().ResultValue);
    }

    [Fact]
    public void Sequence_SingleFailure_IsReturnedUnchangedOrPrefixed()
    {
        Result<int>[] results = [1, QuantityInvalid, 3];

        Assert.Same(QuantityInvalid, results.CombineAll().Error);
        Assert.Equal("lines[1].quantity", results.CombineAll("lines").Error.Field);
    }

    [Fact]
    public void Sequence_SeveralFailures_AreGroupedWithIndexedPaths()
    {
        Result<int>[] results = [QuantityInvalid, 2, Error.Validation("Bad.")];

        var error = results.CombineAll("lines").Error;

        Assert.Equal("validation.failed", error.Code);
        Assert.Equal(["lines[0].quantity", "lines[2]"], error.Details.Select(d => d.Field));
    }

    [Fact]
    public void Sequence_DefaultResult_CountsAsFailure()
    {
        Result<int>[] results = [1, default];

        Assert.Equal(Error.DefaultResult.Code, results.CombineAll("items").Error.Code);
        Assert.Equal("items[1]", results.CombineAll("items").Error.Field);
    }

    [Fact]
    public void Sequence_ValuesAreReadOnly()
    {
        Result<int>[] results = [1];
        var values = (IList<int>)results.CombineAll().ResultValue;

        Assert.Throws<NotSupportedException>(() => values.Add(2));
    }

    [Fact]
    public void Sequence_IsEnumeratedOnce()
    {
        var source = new CountingEnumerable<Result<int>>([1, 2, QuantityInvalid]);

        _ = source.CombineAll();

        Assert.Equal(1, source.Enumerations);
    }

    // CombineAll with a selector

    [Fact]
    public void Selector_ProcessesEveryElement_AndReportsEveryError()
    {
        var calls = 0;
        int[] quantities = [-1, 5, 0];

        var result = quantities.CombineAll(q => { calls++; return Positive(q); }, field: "lines");

        Assert.Equal(3, calls);
        Assert.Equal(["lines[0].quantity", "lines[2].quantity"], result.Error.Details.Select(d => d.Field));
    }

    [Fact]
    public void Selector_ElementWithSeveralErrors_KeepsItsDetails()
    {
        Result<(string, int)> Line((string Product, int Quantity) line) =>
            Result.CombineAll(
                string.IsNullOrEmpty(line.Product) ? Result<string>.Failure(ProductMissing) : line.Product,
                Positive(line.Quantity));

        (string, int)[] lines = [("apple", 1), ("", 0)];

        var error = lines.CombineAll(Line, "lines").Error;              // a single failing line: returned unchanged

        Assert.Equal(["lines[1].product", "lines[1].quantity"], error.Details.Select(d => d.Field));
    }

    [Fact]
    public void Selector_AllSucceed_ReturnsMappedValues()
    {
        int[] quantities = [1, 2, 3];

        Assert.Equal([1, 2, 3], quantities.CombineAll(Positive).ResultValue);
    }

    [Fact]
    public void NullOrBlankArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => ((IEnumerable<Result<int>>)null!).CombineAll());
        Assert.Throws<ArgumentNullException>(() => ((IEnumerable<int>)null!).CombineAll(Positive));
        Assert.Throws<ArgumentNullException>(() => new[] { 1 }.CombineAll((Func<int, Result<int>>)null!));
        Assert.Throws<ArgumentException>(() => new Result<int>[] { 1 }.CombineAll(" "));
        Assert.Throws<ArgumentException>(() => new[] { 1 }.CombineAll(Positive, ""));
        Assert.Throws<ArgumentException>(() => { _ = new[] { 1 }.CombineAllAsync(n => Task.FromResult(Positive(n)), " "); });   // at the call
    }

    // CombineAllAsync

    [Fact]
    public async Task Async_RunsElementsOneAfterAnother()
    {
        var running = 0;
        var maxRunning = 0;

        async Task<Result<int>> Check(int quantity)
        {
            maxRunning = Math.Max(maxRunning, Interlocked.Increment(ref running));
            await Task.Yield();
            Interlocked.Decrement(ref running);
            return Positive(quantity);
        }

        var result = await new[] { 1, 2, 3, 4 }.CombineAllAsync(Check);

        Assert.Equal([1, 2, 3, 4], result.ResultValue);
        Assert.Equal(1, maxRunning);
    }

    [Fact]
    public async Task Async_ReportsEveryErrorWithIndexedPaths()
    {
        async Task<Result<int>> Check(int quantity)
        {
            await Task.Yield();
            return Positive(quantity);
        }

        var error = (await new[] { 0, 1, -2 }.CombineAllAsync(Check, "lines")).Error;

        Assert.Equal(["lines[0].quantity", "lines[2].quantity"], error.Details.Select(d => d.Field));
    }

    [Fact]
    public void Async_NullArguments_ThrowWhenCalled()
    {
        ThrowsEagerly(() => _ = ((IEnumerable<int>)null!).CombineAllAsync(q => Task.FromResult(Positive(q))));
        ThrowsEagerly(() => _ = new[] { 1 }.CombineAllAsync((Func<int, Task<Result<int>>>)null!));
    }

    [Fact]
    public async Task Async_SelectorException_SurfacesWhenAwaited()
    {
        Func<int, Task<Result<int>>> throwing = _ => throw new InvalidOperationException("boom");

        var task = new[] { 1 }.CombineAllAsync(throwing);

        Assert.Equal("boom", (await Assert.ThrowsAsync<InvalidOperationException>(() => task)).Message);
    }

    private sealed class CountingEnumerable<T>(IEnumerable<T> items) : IEnumerable<T>
    {
        public int Enumerations { get; private set; }

        public IEnumerator<T> GetEnumerator()
        {
            Enumerations++;
            return items.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
