using System.Text.Json;
using FluentValidation;
using FluentValidation.Results;
using ResultUnion.FluentValidation;
using ResultUnion.Serialization;

namespace ResultUnion.FluentValidation.Tests;

public sealed class ValidationResultExtensionsTests
{
    public sealed record Line(string? Product, int Quantity);

    public sealed record Address(string? PostCode);

    public sealed record Order(string? Email, string? Password, Address? Address, IReadOnlyList<Line> Lines);

    private sealed class OrderValidator : AbstractValidator<Order>
    {
        public OrderValidator()
        {
            RuleFor(o => o.Email).NotEmpty().WithErrorCode("email.required");
            RuleFor(o => o.Password).MinimumLength(8).WithErrorCode("password.too_short");
            RuleFor(o => o.Address!.PostCode).NotEmpty().WithErrorCode("address.post_code_required").When(o => o.Address is not null);
            RuleForEach(o => o.Lines).ChildRules(line =>
                line.RuleFor(l => l.Quantity).GreaterThan(0).WithErrorCode("order_line.quantity_not_positive"));
        }
    }

    private static readonly Order Valid = new("ann@example.com", "secret123", new Address("0102"), [new Line("tea", 1)]);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---- success ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_valid_instance_is_returned_as_the_success()
    {
        var validator = new OrderValidator();

        Assert.True(new ValidationResult().ToResult().IsSuccess);
        Assert.True(validator.ValidateToResult(Valid).TryGetResultValue(out var value));
        Assert.Same(Valid, value);
        Assert.True((await validator.ValidateToResultAsync(Valid, Ct)).TryGetResultValue(out var awaited));
        Assert.Same(Valid, awaited);
    }

    // ---- one failure -----------------------------------------------------------------------------------------------

    [Fact]
    public void One_failure_is_returned_as_it_is_with_code_field_message_and_arguments()
    {
        var result = new OrderValidator().ValidateToResult(Valid with { Password = "short" });

        Assert.True(result.TryGetError(out var error));
        Assert.Equal("password.too_short", error.Code);
        Assert.Equal(ErrorType.Validation, error.Type);
        Assert.Equal("password", error.Field);
        Assert.Contains("8", error.Message, StringComparison.Ordinal);
        Assert.Empty(error.Details);
        Assert.Equal(8, error.Arguments["minLength"]);
        Assert.Equal(5, error.Arguments["totalLength"]);
    }

    [Fact]
    public void The_attempted_value_and_property_placeholders_are_never_copied()
    {
        Assert.True(new OrderValidator().ValidateToResult(Valid with { Password = "hunter2" }).TryGetError(out var error));

        Assert.DoesNotContain("hunter2", error.Arguments.Values.OfType<string>());
        Assert.DoesNotContain(error.Arguments.Keys, key => key is "propertyValue" or "propertyName" or "propertyPath");
    }

    [Fact]
    public void Without_WithErrorCode_the_code_is_the_validator_name()
    {
        var validator = new InlineValidator<Order>();
        validator.RuleFor(o => o.Email).NotEmpty();

        Assert.True(validator.ValidateToResult(Valid with { Email = "" }).TryGetError(out var error));
        Assert.Equal("NotEmptyValidator", error.Code);
    }

    // ---- several failures ------------------------------------------------------------------------------------------

    [Fact]
    public void Several_failures_are_grouped_with_one_field_error_each()
    {
        var invalid = new Order("", "short", new Address(""), [new Line("tea", 1), new Line("cake", 0)]);

        Assert.True(new OrderValidator().ValidateToResult(invalid).TryGetError(out var error));

        Assert.Equal("validation.failed", error.Code);
        Assert.Equal(
            new Dictionary<string, string>
            {
                ["email"] = "email.required",
                ["password"] = "password.too_short",
                ["address.postCode"] = "address.post_code_required",
                ["lines[1].quantity"] = "order_line.quantity_not_positive",
            },
            error.Details.ToDictionary(d => d.Field!, d => d.Code));
    }

    [Fact]
    public void The_index_of_a_collection_item_is_in_the_field_not_in_the_arguments()
    {
        Assert.True(new OrderValidator().ValidateToResult(Valid with { Lines = [new Line("cake", 0)] }).TryGetError(out var error));

        Assert.Equal("lines[0].quantity", error.Field);
        Assert.DoesNotContain(error.Arguments.Keys, key => key == "collectionIndex");
    }

    [Fact]
    public void A_failure_without_code_or_message_still_becomes_a_valid_error()
    {
        var validation = new ValidationResult([new ValidationFailure("Email", "") { ErrorCode = "" }]);

        Assert.True(validation.ToResult().TryGetError(out var error));
        Assert.Equal("validation.failed", error.Code);
        Assert.Equal("validation.failed", error.Message);
        Assert.Equal("email", error.Field);
    }

    [Fact]
    public void A_null_instance_is_rejected_when_the_method_is_called()
    {
        var validator = new OrderValidator();

        Assert.Throws<ArgumentNullException>("instance", () => validator.ValidateToResult(null!));
        Assert.Throws<ArgumentNullException>("instance", () => { _ = validator.ValidateToResultAsync(null!, Ct); });
    }

    // ---- field names -----------------------------------------------------------------------------------------------

    [Fact]
    public void Field_names_can_be_chosen_by_the_caller()
    {
        var validation = new OrderValidator().Validate(Valid with { Email = "" });

        Assert.True(validation.ToResult(path => path.ToUpperInvariant()).TryGetError(out var error));
        Assert.Equal("EMAIL", error.Field);
    }

    [Fact]
    public void A_failure_without_a_property_has_no_field()
    {
        var validator = new InlineValidator<Order>();
        validator.RuleFor(o => o).Must(o => o.Lines.Count > 0).WithErrorCode("order.empty").OverridePropertyName("");

        Assert.True(validator.ValidateToResult(Valid with { Lines = [] }).TryGetError(out var error));
        Assert.Equal("order.empty", error.Code);
        Assert.Null(error.Field);
    }

    // ---- arguments -------------------------------------------------------------------------------------------------

    [Fact]
    public void Only_numbers_strings_and_booleans_become_arguments()
    {
        var validator = new InlineValidator<Line>();
        validator.RuleFor(l => l.Quantity).InclusiveBetween(1, 10).WithErrorCode("quantity.out_of_range");
        validator.RuleFor(l => l.Product).Must(_ => false).WithErrorCode("product.rejected")
            .WithMessage("Rejected on {When}.").WithState(_ => "state")
            .Configure(rule => rule.MessageBuilder = context =>
            {
                context.MessageFormatter.AppendArgument("When", new DateTime(2026, 1, 1));
                context.MessageFormatter.AppendArgument("Limit", 3);
                return context.GetDefaultMessage();
            });

        Assert.True(validator.ValidateToResult(new Line("tea", 0)).TryGetError(out var error));

        var range = error.Details.Single(d => d.Code == "quantity.out_of_range");
        Assert.Equal(1, range.Arguments["from"]);
        Assert.Equal(10, range.Arguments["to"]);
        var rejected = error.Details.Single(d => d.Code == "product.rejected");
        Assert.Equal(3, rejected.Arguments["limit"]);
        Assert.False(rejected.Arguments.ContainsKey("when"));
    }

    [Fact]
    public void The_error_survives_a_JSON_round_trip_unchanged()
    {
        var options = new JsonSerializerOptions { Converters = { new ErrorJsonConverter() } };
        var invalid = new Order("", "short", null, [new Line("cake", 0)]);
        Assert.True(new OrderValidator().ValidateToResult(invalid).TryGetError(out var error));

        var copy = JsonSerializer.Deserialize<Error>(JsonSerializer.Serialize(error, options), options);

        Assert.Equal(error, copy);
    }

    // ---- severity, async, cancellation -----------------------------------------------------------------------------

    [Fact]
    public void A_warning_fails_the_result_too_as_IsValid_does()
    {
        var validator = new InlineValidator<Order>();
        validator.RuleFor(o => o.Email).NotEmpty().WithSeverity(Severity.Warning).WithErrorCode("email.recommended");

        var validation = validator.Validate(Valid with { Email = "" });

        Assert.False(validation.IsValid);
        Assert.True(validation.ToResult().IsFailure);
    }

    [Fact]
    public async Task Asynchronous_rules_run_with_ValidateToResultAsync()
    {
        var validator = new InlineValidator<Order>();
        validator.RuleFor(o => o.Email).MustAsync(async (email, ct) =>
        {
            await Task.Yield();
            return email != "taken@example.com";
        }).WithErrorCode("email.taken");

        Assert.True((await validator.ValidateToResultAsync(Valid with { Email = "taken@example.com" }, Ct)).TryGetError(out var error));
        Assert.Equal("email.taken", error.Code);
        Assert.Throws<AsyncValidatorInvokedSynchronouslyException>(() => validator.ValidateToResult(Valid));
    }

    [Fact]
    public async Task Cancellation_is_not_turned_into_a_failure()
    {
        var validator = new InlineValidator<Order>();
        validator.RuleFor(o => o.Email).MustAsync(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return true;
        });
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => validator.ValidateToResultAsync(Valid, cancelled.Token));
    }
}
