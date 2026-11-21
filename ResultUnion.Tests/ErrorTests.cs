namespace ResultUnion.Tests;

public class ErrorTests
{
    public static TheoryData<Func<string, Error>, string, ErrorType> Factories => new()
    {
        { Error.Unexpected, "unexpected", ErrorType.Unexpected },
        { Error.Validation, "validation.failed", ErrorType.Validation },
        { Error.NotFound, "resource.not_found", ErrorType.NotFound },
        { Error.Unauthorized, "unauthorized", ErrorType.Unauthorized },
        { Error.Forbidden, "forbidden", ErrorType.Forbidden },
        { Error.Conflict, "conflict", ErrorType.Conflict },
        { Error.Concurrency, "concurrency.conflict", ErrorType.Concurrency },
        { Error.Unavailable, "dependency.unavailable", ErrorType.Unavailable },
        { Error.BusinessRule, "business_rule.violated", ErrorType.BusinessRule },
        { Error.TooManyRequests, "rate_limit.exceeded", ErrorType.TooManyRequests },
    };

    [Theory]
    [MemberData(nameof(Factories))]
    public void Factory_SetsCodeTypeAndMessage(Func<string, Error> factory, string code, ErrorType type)
    {
        var error = factory("Something happened.");

        Assert.Equal(code, error.Code);
        Assert.Equal(type, error.Type);
        Assert.Equal("Something happened.", error.Message);
    }

    [Fact]
    public void Create_UsesCustomCode()
    {
        var error = Error.Create("deposit.already_closed", "Deposit is closed.", ErrorType.Conflict);

        Assert.Equal("deposit.already_closed", error.Code);
        Assert.Equal(ErrorType.Conflict, error.Type);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_BlankCodeOrMessage_Throws(string? blank)
    {
        Assert.ThrowsAny<ArgumentException>(() => Error.Create(blank!, "message", ErrorType.Unexpected));
        Assert.ThrowsAny<ArgumentException>(() => Error.Create("code", blank!, ErrorType.Unexpected));
    }

    [Fact]
    public void Equality_IsByValue()
    {
        Assert.Equal(Error.NotFound("x"), Error.NotFound("x"));
        Assert.NotEqual(Error.NotFound("x"), Error.NotFound("y"));
    }

    [Fact]
    public void ToString_IsCodeAndMessage()
    {
        Assert.Equal("conflict: Already exists.", Error.Conflict("Already exists.").ToString());
    }
}
