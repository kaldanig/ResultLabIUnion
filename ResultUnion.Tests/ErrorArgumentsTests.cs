namespace ResultUnion.Tests;

/// <summary><see cref="Error.Arguments"/> and <see cref="Error.WithArgument(string, object)"/>: the values behind a message, for localization.</summary>
public class ErrorArgumentsTests
{
    private static Error TooYoung(int age) =>
        Error.Create("user.too_young", $"Users must be at least 18; this one is {age}.", ErrorType.BusinessRule)
            .WithArgument("minimumAge", 18)
            .WithArgument("age", age);

    [Fact]
    public void NewError_HasNoArguments()
    {
        Assert.NotNull(Error.NotFound("x").Arguments);
        Assert.Empty(Error.NotFound("x").Arguments);
    }

    [Fact]
    public void WithArgument_AddsNamedValues()
    {
        var error = TooYoung(15);

        Assert.Equal(18, error.Arguments["minimumAge"]);
        Assert.Equal(15, error.Arguments["age"]);
        Assert.Equal(2, error.Arguments.Count);
    }

    [Fact]
    public void WithArgument_ReturnsACopy()
    {
        var original = Error.Validation("x");
        var withArgument = original.WithArgument("limit", 3);

        Assert.Empty(original.Arguments);
        Assert.Single(withArgument.Arguments);
    }

    [Fact]
    public void WithArgument_SameName_ReplacesTheValue_AndNullIsAllowed()
    {
        var error = Error.Validation("x").WithArgument("limit", 3).WithArgument("limit", 5).WithArgument("hint", null);

        Assert.Equal(5, error.Arguments["limit"]);
        Assert.Null(error.Arguments["hint"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void WithArgument_BlankName_Throws(string? name)
    {
        Assert.ThrowsAny<ArgumentException>(() => Error.Validation("x").WithArgument(name!, 1));
    }

    [Fact]
    public void Arguments_AreReadOnly()
    {
        var arguments = (IDictionary<string, object?>)TooYoung(15).Arguments;

        Assert.Throws<NotSupportedException>(() => arguments.Add("extra", 1));
    }

    [Fact]
    public void Arguments_SurviveFieldChanges_AndGrouping()
    {
        var error = TooYoung(15).WithField("age").WithFieldPrefix("users[0]");

        Assert.Equal("users[0].age", error.Field);
        Assert.Equal(15, error.Arguments["age"]);
        Assert.Equal(15, Error.Validation([error]).Details[0].Arguments["age"]);
        Assert.Equal(15, Error.Validation([error]).WithFieldPrefix("batch").Flatten().Single().Arguments["age"]);
    }

    [Fact]
    public void Equality_ComparesArgumentsByNameAndValue_InAnyOrder()
    {
        var first = Error.Validation("x").WithArgument("a", 1).WithArgument("b", "two");
        var reordered = Error.Validation("x").WithArgument("b", "two").WithArgument("a", 1);

        Assert.Equal(first, reordered);
        Assert.Equal(first.GetHashCode(), reordered.GetHashCode());
        Assert.NotEqual(first, Error.Validation("x").WithArgument("a", 1).WithArgument("b", "three"));
        Assert.NotEqual(first, Error.Validation("x").WithArgument("a", 1));
        Assert.NotEqual(first, Error.Validation("x"));
    }

    // Typical client-side use: a translated template per code, filled from the arguments.

    [Fact]
    public void Arguments_FillALocalizedTemplate()
    {
        var templates = new Dictionary<string, string> { ["user.too_young"] = "მომხმარებელი უნდა იყოს მინიმუმ {minimumAge} წლის, არის {age}." };
        var error = TooYoung(15);

        var text = error.Arguments.Aggregate(templates[error.Code], (current, pair) => current.Replace($"{{{pair.Key}}}", $"{pair.Value}"));

        Assert.Equal("მომხმარებელი უნდა იყოს მინიმუმ 18 წლის, არის 15.", text);
    }
}
