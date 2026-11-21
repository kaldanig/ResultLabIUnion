using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ResultUnion.AspNetCore;

namespace ResultUnion.Tests.AspNetCore;

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
    [InlineData(ErrorType.Unavailable, 503)]
    [InlineData(ErrorType.Unexpected, 500)]
    [InlineData((ErrorType)99, 500)]
    public void ToStatusCode_MapsEveryErrorType(ErrorType type, int expected)
    {
        Assert.Equal(expected, type.ToStatusCode());
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
