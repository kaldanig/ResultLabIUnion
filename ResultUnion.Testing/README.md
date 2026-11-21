# ResultUnion.Testing

Test assertions for [ResultUnion](https://www.nuget.org/packages/ResultUnion). A success gives you its value, a failure
its error, and anything unexpected fails the test with the whole result in the message. Works with xUnit, NUnit, MSTest
and any other framework: a failed assertion throws `ResultAssertionException`.

```csharp
using ResultUnion.Testing;

var email = Email.Create("ann@example.com").ShouldSucceed();           // the Email

Email.Create("").ShouldFail("email.required")                           // the Error
    .ShouldHaveType(ErrorType.Validation);

await service.RegisterAsync(command, ct).ShouldFailAsync("user.email_taken");

UserService.Validate(new RegisterUser("", "short", "34")).ShouldFail()
    .ShouldHaveFieldErrors(("email", "email.required"), ("password", "password.too_short"));

Password.Create("short").ShouldFail().ShouldHaveArgument("minimumLength", 8);
```

A failure reads like this:

```text
Expected the error code 'email.required', but the error is email.invalid_format: 'ann' is not a valid email.
(Validation, arguments value="ann").
```

- Documentation: <https://kaldanig.github.io/ResultLabIUnion/>
- Source: <https://github.com/kaldanig/ResultLabIUnion>
