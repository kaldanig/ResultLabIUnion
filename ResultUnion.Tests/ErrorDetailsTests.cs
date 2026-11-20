namespace ResultUnion.Tests;

/// <summary><see cref="Error.Field"/>, <see cref="Error.Details"/> and the value equality that covers them.</summary>
public class ErrorDetailsTests
{
    private static readonly Error EmailRequired = Error.Validation("Email is required.").WithField("email");
    private static readonly Error AgeInvalid = Error.Validation("'abc' is not a number.").WithField("age");

    // Defaults

    [Fact]
    public void NewError_HasNoFieldAndNoDetails()
    {
        var error = Error.NotFound("x");

        Assert.Null(error.Field);
        Assert.NotNull(error.Details);
        Assert.Empty(error.Details);
    }

    // WithField

    [Fact]
    public void WithField_ReturnsCopyWithField_AndKeepsTheRest()
    {
        var original = Error.Conflict("Already exists.");
        var named = original.WithField("email");

        Assert.Equal("email", named.Field);
        Assert.Equal(original.Code, named.Code);
        Assert.Equal(original.Message, named.Message);
        Assert.Equal(original.Type, named.Type);
        Assert.Null(original.Field);
    }

    [Fact]
    public void WithField_KeepsDetails()
    {
        var group = Error.Validation([EmailRequired, AgeInvalid]).WithField("form");

        Assert.Equal("form", group.Field);
        Assert.Equal([EmailRequired, AgeInvalid], group.Details);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void WithField_BlankField_Throws(string? field)
    {
        Assert.ThrowsAny<ArgumentException>(() => EmailRequired.WithField(field!));
    }

    // Validation(details)

    [Fact]
    public void ValidationGroup_HoldsDetailsInOrder()
    {
        var group = Error.Validation([EmailRequired, AgeInvalid]);

        Assert.Equal("validation.failed", group.Code);
        Assert.Equal(ErrorType.Validation, group.Type);
        Assert.Null(group.Field);
        Assert.Equal([EmailRequired, AgeInvalid], group.Details);
    }

    [Fact]
    public void ValidationGroup_IsValidationEvenWhenDetailsAreNot()
    {
        var group = Error.Validation([Error.NotFound("x"), Error.Conflict("y")]);

        Assert.Equal(ErrorType.Validation, group.Type);
    }

    [Fact]
    public void ValidationGroup_CopiesTheInput()
    {
        var source = new List<Error> { EmailRequired };
        var group = Error.Validation(source);

        source.Add(AgeInvalid);

        Assert.Single(group.Details);
    }

    [Fact]
    public void ValidationGroup_DetailsAreReadOnly()
    {
        var details = (IList<Error>)Error.Validation([EmailRequired]).Details;

        Assert.Throws<NotSupportedException>(() => details.Add(AgeInvalid));
    }

    [Fact]
    public void ValidationGroup_InvalidInput_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => Error.Validation((IEnumerable<Error>)null!));
        Assert.Throws<ArgumentException>(() => Error.Validation(Array.Empty<Error>()));
        Assert.Throws<ArgumentNullException>(() => Error.Validation([EmailRequired, null!]));
    }

    // Equality

    [Fact]
    public void Equality_IncludesField()
    {
        Assert.Equal(Error.Validation("x").WithField("a"), Error.Validation("x").WithField("a"));
        Assert.NotEqual(Error.Validation("x").WithField("a"), Error.Validation("x").WithField("b"));
        Assert.NotEqual(Error.Validation("x").WithField("a"), Error.Validation("x"));
    }

    [Fact]
    public void Equality_ComparesDetailsElementByElement()
    {
        var first = Error.Validation([EmailRequired, AgeInvalid]);
        var same = Error.Validation([Error.Validation("Email is required.").WithField("email"), AgeInvalid]);
        var reordered = Error.Validation([AgeInvalid, EmailRequired]);

        Assert.Equal(first, same);
        Assert.Equal(first.GetHashCode(), same.GetHashCode());
        Assert.NotEqual(first, reordered);
        Assert.True(first == same);
        Assert.False(first.Equals(null));
    }

    [Fact]
    public void Equality_ResultsCarryingEqualGroupsAreEqual()
    {
        Assert.Equal(Result.Failure(Error.Validation([EmailRequired])), Result.Failure(Error.Validation([EmailRequired])));
    }

    // ToString

    [Fact]
    public void ToString_ShowsFieldAndDetails()
    {
        Assert.Equal("validation.failed: Email is required. (field 'email')", EmailRequired.ToString());
        Assert.Equal(
            "validation.failed: One or more validation errors occurred. [validation.failed: Email is required. (field 'email'); validation.failed: 'abc' is not a number. (field 'age')]",
            Error.Validation([EmailRequired, AgeInvalid]).ToString());
    }
}
