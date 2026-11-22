using ResultUnionApi.Products;

namespace ResultUnionApi.Tests;

/// <summary>
/// The use cases without HTTP. ResultUnion.Testing's assertions return the value or the error, so a test reads the
/// outcome in one line and asserts on codes and fields, never on messages.
/// </summary>
public sealed class ProductServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly ProductService _service = new(new InMemoryProductRepository());

    [Fact]
    public async Task A_valid_product_is_created()
    {
        var product = await _service.CreateAsync(new CreateProduct("  Coffee ", 4.5m), Ct).ShouldSucceedAsync();

        Assert.Equal("Coffee", product.Name.Value);
        Assert.Equal(4.5m, product.Price.Amount);
    }

    [Fact]
    public async Task Every_invalid_field_is_reported_at_once()
    {
        var error = await _service.CreateAsync(new CreateProduct("", -1m), Ct).ShouldFailAsync("validation.failed");

        error.ShouldHaveFieldErrors(("name", "product_name.required"), ("price", "price.not_positive"));
    }

    [Fact]
    public async Task A_name_that_is_taken_is_a_conflict()
    {
        await _service.CreateAsync(new CreateProduct("Coffee", 4.5m), Ct).ShouldSucceedAsync();

        var error = await _service.CreateAsync(new CreateProduct("coffee", 5m), Ct).ShouldFailAsync("product.name_taken");

        error.ShouldHaveType(ErrorType.Conflict).ShouldHaveArgument("name", "coffee");
    }

    [Fact]
    public async Task A_created_product_can_be_found_and_an_unknown_one_cannot()
    {
        var created = await _service.CreateAsync(new CreateProduct("Tea", 3m), Ct).ShouldSucceedAsync();
        var unknown = Guid.NewGuid();

        Assert.Same(created, await _service.FindAsync(created.Id, Ct).ShouldSucceedAsync());
        (await _service.FindAsync(unknown, Ct).ShouldFailAsync("product.not_found")).ShouldHaveArgument("id", unknown);
    }

    [Theory]
    [InlineData(null, "product_name.required")]
    [InlineData("   ", "product_name.required")]
    public void A_missing_name_is_required(string? name, string code) =>
        ProductName.Create(name).ShouldFail(code).ShouldHaveField(null);   // the caller sets the field

    [Fact]
    public void A_long_name_says_how_long_it_may_be() =>
        ProductName.Create(new string('x', ProductName.MaxLength + 1))
            .ShouldFail("product_name.too_long")
            .ShouldHaveArgument("maxLength", ProductName.MaxLength);
}
