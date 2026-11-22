using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ResultUnion.AspNetCore;

namespace ResultUnion.AspNetCore.Tests;

public class ProblemDetailsMappingTests
{
    [Theory]
    [InlineData(ErrorType.Validation, 400)]
    [InlineData(ErrorType.Unauthorized, 401)]
    [InlineData(ErrorType.Forbidden, 403)]
    [InlineData(ErrorType.NotFound, 404)]
    [InlineData(ErrorType.Conflict, 409)]
    [InlineData(ErrorType.Concurrency, 409)]
    [InlineData(ErrorType.BusinessRule, 422)]
    [InlineData(ErrorType.TooManyRequests, 429)]
    [InlineData(ErrorType.Unavailable, 503)]
    [InlineData(ErrorType.Unexpected, 500)]
    [InlineData((ErrorType)99, 500)]
    public void ToStatusCode_MapsEveryErrorType(ErrorType type, int expected)
    {
        Assert.Equal(expected, type.ToHttpStatusCode());
    }

    [Fact]
    public void SingleError_BecomesPlainProblemDetailsWithCode()
    {
        var problem = Error.NotFound("No user 7.").ToProblemDetails();

        Assert.IsNotType<HttpValidationProblemDetails>(problem);
        Assert.Equal(404, problem.Status);
        Assert.Equal("No user 7.", problem.Detail);
        Assert.Equal("resource.not_found", problem.Extensions["code"]);
        Assert.False(problem.Extensions.ContainsKey("details"));
    }

    [Theory]
    [InlineData(ErrorType.Conflict)]
    [InlineData(ErrorType.Concurrency)]
    [InlineData(ErrorType.Unexpected)]
    public void The_category_names_the_error_type_which_the_status_alone_does_not(ErrorType type)
    {
        var problem = Error.Create("x.y", "Message.", type).ToProblemDetails();

        Assert.Equal(type.ToString(), problem.Extensions["category"]);
    }

    [Fact]
    public void ErrorWithField_BecomesValidationProblemDetails()
    {
        var problem = Assert.IsType<HttpValidationProblemDetails>(
            Error.Validation("Email is required.").WithField("email").ToProblemDetails());

        Assert.Equal(400, problem.Status);
        Assert.Equal(["Email is required."], problem.Errors["email"]);
        Assert.Equal(
            [new ProblemErrorDetail("validation.failed", "email", "Email is required.")],
            Assert.IsType<ProblemErrorDetail[]>(problem.Extensions["details"]));
    }

    [Fact]
    public void GroupedErrors_AreListedPerField_WithCodes()
    {
        var group = Error.Validation(
        [
            Error.Create("email.invalid_format", "Not an email.", ErrorType.Validation).WithField("email"),
            Error.Create("password.too_short", "Too short.", ErrorType.Validation).WithField("password"),
            Error.Create("password.no_digit", "Needs a digit.", ErrorType.Validation).WithField("password"),
            Error.Create("form.expired", "The form expired.", ErrorType.Validation),
        ]);

        var problem = Assert.IsType<HttpValidationProblemDetails>(group.ToProblemDetails());

        Assert.Equal("validation.failed", problem.Extensions["code"]);
        Assert.Equal(["Not an email."], problem.Errors["email"]);
        Assert.Equal(["Too short.", "Needs a digit."], problem.Errors["password"]);
        Assert.Equal(["The form expired."], problem.Errors[""]);

        var details = Assert.IsType<ProblemErrorDetail[]>(problem.Extensions["details"]);
        Assert.Equal(["email.invalid_format", "password.too_short", "password.no_digit", "form.expired"], details.Select(d => d.Code));
    }

    [Fact]
    public void NestedDetails_AreFlattenedToTheInnermostErrors()
    {
        var quantity = Error.Create("line.quantity_invalid", "Quantity must be positive.", ErrorType.Validation).WithField("quantity");
        var product = Error.Create("line.product_required", "Product is required.", ErrorType.Validation).WithField("product");

        int[] quantities = [0, 5, -1];
        var error = quantities.CombineAll(
            q => q > 0 ? Result<int>.Success(q) : Error.Validation([product, quantity]),
            field: "lines").Error;

        var problem = Assert.IsType<HttpValidationProblemDetails>(error.ToProblemDetails());

        Assert.Equal(["lines[0].product", "lines[0].quantity", "lines[2].product", "lines[2].quantity"], problem.Errors.Keys);
        Assert.Equal(["Quantity must be positive."], problem.Errors["lines[2].quantity"]);
        Assert.Equal(4, Assert.IsType<ProblemErrorDetail[]>(problem.Extensions["details"]).Length);
    }

    [Fact]
    public void Arguments_AreAnExtension_AndPartOfEachDetail()
    {
        var tooYoung = Error.Create("user.too_young", "Too young.", ErrorType.BusinessRule).WithArgument("minimumAge", 18).WithArgument("age", 15);
        var tooShort = Error.Create("password.too_short", "Too short.", ErrorType.Validation).WithArgument("minimumLength", 8).WithField("password");
        var noArguments = Error.Create("email.required", "Required.", ErrorType.Validation).WithField("email");

        var single = tooYoung.ToProblemDetails();
        var arguments = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(single.Extensions["arguments"]);
        Assert.Equal(18, arguments["minimumAge"]);

        var details = Assert.IsType<ProblemErrorDetail[]>(Error.Validation([tooShort, noArguments]).ToProblemDetails().Extensions["details"]);
        Assert.Equal(8, details[0].Arguments!["minimumLength"]);
        Assert.Null(details[1].Arguments);
    }

    [Fact]
    public void Details_are_equal_by_value_including_their_arguments()
    {
        var minimum = new Dictionary<string, object?> { ["minimum"] = 8 };
        var detail = new ProblemErrorDetail("password.too_short", "password", "Too short.", minimum);

        Assert.Equal(detail, new ProblemErrorDetail("password.too_short", "password", "Too short.", new Dictionary<string, object?> { ["minimum"] = 8 }));
        Assert.Equal(detail.GetHashCode(), new ProblemErrorDetail("password.too_short", "password", "Too short.", new Dictionary<string, object?> { ["minimum"] = 8 }).GetHashCode());
        Assert.Equal(new ProblemErrorDetail("a", null, "m"), new ProblemErrorDetail("a", null, "m", new Dictionary<string, object?>())); // none = empty

        Assert.NotEqual(detail, new ProblemErrorDetail("password.too_short", "password", "Too short.", new Dictionary<string, object?> { ["minimum"] = 10 }));
        Assert.NotEqual(detail, new ProblemErrorDetail("password.too_short", "password", "Too short.", new Dictionary<string, object?> { ["maximum"] = 8 }));
        Assert.NotEqual(detail, new ProblemErrorDetail("password.too_short", "password", "Too short."));
        Assert.NotEqual(detail, new ProblemErrorDetail("password.weak", "password", "Too short.", minimum));
        Assert.NotEqual(detail, new ProblemErrorDetail("password.too_short", "secret", "Too short.", minimum));
        Assert.NotEqual(detail, new ProblemErrorDetail("password.too_short", "password", "Short.", minimum));
        Assert.False(detail.Equals(null));
    }

    [Fact]
    public void ErrorWithoutArguments_HasNoArgumentsExtension()
    {
        Assert.False(Error.NotFound("x").ToProblemDetails().Extensions.ContainsKey("arguments"));
    }

    [Fact]
    public void Unexpected_DoesNotSendArguments()
    {
        var problem = Error.Unexpected("Failed at row 7.").WithArgument("row", 7).ToProblemDetails();

        Assert.False(problem.Extensions.ContainsKey("arguments"));
    }

    [Fact]
    public void Unexpected_HidesTheMessage()
    {
        var problem = Error.Unexpected("NullReferenceException at Foo.Bar()").ToProblemDetails();

        Assert.Equal(500, problem.Status);
        Assert.Equal(ProblemDetailsMapping.UnexpectedDetail, problem.Detail);
        Assert.Equal("unexpected", problem.Extensions["code"]);
    }

    [Fact]
    public void ToProblemDetails_NullError_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ((Error)null!).ToProblemDetails());
    }
}
