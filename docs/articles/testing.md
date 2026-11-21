# Testing

Code that returns results is easier to test than code that throws: a failure is an ordinary return value, so a test
asserts on it like on any other. This page shows what to assert and how, with xUnit; the same applies to any test
framework.

## Assert on the code, not the message

The error code is the contract (clients switch on it), the message is text for people and may be reworded or
translated. Assert on what a client depends on: the code, the `ErrorType` and, for input errors, the field.

```csharp
[Fact]
public void An_empty_email_is_required()
{
    var result = Email.Create("  ");

    Assert.True(result.TryGetError(out var error));
    Assert.Equal("email.required", error.Code);
    Assert.Equal(ErrorType.Validation, error.Type);
}
```

When the expected error is a fixed catalog entry, compare with it directly. Errors compare by value (code, message,
type, field, arguments, details):

```csharp
Assert.True(Email.Create(null).TryGetError(out var error));
Assert.Equal(Email.Errors.Required, error);
```

For errors with arguments, check the arguments a client uses to build its message:

```csharp
Assert.True(Email.Create("ann.example.com").TryGetError(out var error));
Assert.Equal("email.invalid_format", error.Code);
Assert.Equal("ann.example.com", error.Arguments["value"]);
```

## Assert on the value

`TryGetResultValue` checks and reads in one step; when the test expects a success, a failure fails the assertion
with the error's code in the message if you write it this way:

```csharp
var result = Email.Create(" Ann@Example.com ");

Assert.True(result.TryGetResultValue(out var email), result.IsFailure ? result.Error.Code : null);
Assert.Equal("ann@example.com", email.Value);
```

A `switch` works too, and is the shortest when both outcomes need checking:

```csharp
var text = Email.Create(input) switch
{
    Email email => email.Value,
    Error error => error.Code,
};
Assert.Equal(expected, text);
```

## Several errors at once

`CombineAll` reports every failure: one failure comes back unchanged, several are grouped in a `validation.failed`
error whose `Details` hold one entry per input. Turn them into field → code pairs and compare the whole set, so a test
also fails when an unexpected extra error appears:

```csharp
var result = UserService.Validate(new RegisterUser(Email: "", Password: "short", Age: "34"));

Assert.True(result.TryGetError(out var error));
Assert.Equal(
    new Dictionary<string, string> { ["email"] = "email.required", ["password"] = "password.too_short" },
    error.Flatten().ToDictionary(e => e.Field!, e => e.Code));
```

`Flatten` also unpacks nested groups, such as a collection whose items each report several fields
(`lines[1].quantity`).

## Async pipelines

An async use case is tested by awaiting it, exactly like the synchronous one. Test both a completed and a pending
dependency when the code under test is a reusable building block; for use cases, the fakes you already have are enough:

```csharp
[Fact]
public async Task A_taken_email_is_a_conflict()
{
    var service = new UserService(new FakeUserRepository(existing: "ann@example.com"));

    var result = await service.RegisterAsync(new RegisterUser("ann@example.com", "secret123", "34"), CancellationToken.None);

    Assert.True(result.TryGetError(out var error));
    Assert.Equal(User.Errors.EmailTaken("ann@example.com"), error);
}
```

## HTTP endpoints

Over HTTP the error arrives as a problem response, and a client sees the `code` and `errors` members. An integration
test with `WebApplicationFactory` checks exactly that:

```csharp
public sealed class RegisterEndpointTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task An_invalid_registration_is_a_validation_problem()
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await factory.CreateClient().PostAsJsonAsync("/users", new { email = "", password = "secret123", age = "34" }, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("email.required", problem.GetProperty("code").GetString());
    }
}
```

## Shorter with ResultUnion.Testing

The `ResultUnion.Testing` package wraps these checks in assertions that return what the next line needs and, when they
fail, print the whole result. It works with any test framework: a failed assertion throws `ResultAssertionException`.

```powershell
dotnet add package ResultUnion.Testing
```

The tests above become:

```csharp
using ResultUnion.Testing;

[Fact]
public void An_empty_email_is_required() =>
    Email.Create("  ").ShouldFail("email.required").ShouldHaveType(ErrorType.Validation);

[Fact]
public void A_valid_email_is_normalized() =>
    Assert.Equal("ann@example.com", Email.Create(" Ann@Example.com ").ShouldSucceed().Value);

[Fact]
public void Several_errors_are_reported_at_once() =>
    UserService.Validate(new RegisterUser(Email: "", Password: "short", Age: "34")).ShouldFail()
        .ShouldHaveFieldErrors(("email", "email.required"), ("password", "password.too_short"));

[Fact]
public async Task A_taken_email_is_a_conflict()
{
    var service = new UserService(new FakeUserRepository(existing: "ann@example.com"));

    await service.RegisterAsync(new RegisterUser("ann@example.com", "secret123", "34"), CancellationToken.None)
        .ShouldFailAsync("user.email_taken");
}
```

A failure names what differs and shows the error in full. `Email.Create("ann").ShouldFail("email.required")` reports:

```text
Expected the error code 'email.required', but the error is email.invalid_format: 'ann' is not a valid email. (Validation, arguments value="ann").
```

`ShouldHaveArgument("minimumLength", 8)` compares numbers by value, so an argument that came back from JSON as a
`long` or a `double` still matches. `ShouldHaveFieldErrors` compares the innermost errors as a set, in any order, and
lists both the missing and the unexpected ones.

## Analyzers in test projects

Analyzer RU0004 warns when `ResultValue` or `Error` is read without checking the result first. In tests such a read is
often deliberate (the test fails with an exception if the result is on the wrong track), so many projects turn the rule
off for test code in `.editorconfig`:

```ini
[*Tests/**.cs]
dotnet_diagnostic.RU0004.severity = none
```

Keep RU0001 (a discarded result) on: a test that ignores a result usually asserts nothing.
