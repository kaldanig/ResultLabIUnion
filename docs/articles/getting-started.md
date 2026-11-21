# Getting started

This walk-through builds one small feature, registering a user, from a value object to an HTTP endpoint. It takes about
fifteen minutes and shows the parts of ResultUnion you will use every day. The [guide](../../README.md) describes every
member; this page shows how they fit together.

## Install

ResultUnion needs .NET 11 (C# 15 unions).

```powershell
dotnet add package ResultUnion
```

```powershell
dotnet add package ResultUnion.AspNetCore
```

The second package is only for ASP.NET Core. The analyzers come with either package; there is nothing else to set up.

## 1. A value object that can fail

An email address arrives as a string that may be empty or malformed. Instead of throwing, `Create` returns
`Result<Email>`: either an `Email` or an `Error`.

```csharp
using ResultUnion;

public sealed record Email
{
    public static class Errors
    {
        public static readonly Error Required =
            Error.Create("email.required", "Email is required.", ErrorType.Validation);

        public static Error InvalidFormat(string value) =>
            Error.Create("email.invalid_format", $"'{value}' is not a valid email.", ErrorType.Validation)
                .WithArgument("value", value);
    }

    public string Value { get; }

    private Email(string value) => Value = value;

    public static Result<Email> Create(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return Errors.Required;

        var trimmed = input.Trim();
        return trimmed.Contains('@')
            ? new Email(trimmed.ToLowerInvariant())
            : Errors.InvalidFormat(trimmed);
    }
}
```

Three conventions are already visible:

- **Both cases convert implicitly.** `return Errors.Required;` and `return new Email(…);` both produce a
  `Result<Email>`, so a factory reads like ordinary code.
- **Errors live in a catalog.** `Email.Errors` holds every way an email can be invalid, each with a stable code
  (`email.required`). Clients switch on codes and translate them, so codes are a contract; messages are for people.
- **Values go into arguments.** `InvalidFormat` adds the rejected value with `WithArgument`, so a client can build its
  own translated message from the code and the arguments.

`ErrorType` says what the caller should do: `Validation` (fix the input), `NotFound`, `Conflict`, `BusinessRule` and
so on. The HTTP and gRPC integrations turn it into a status code.

## 2. Reading a result

A `Result<T>` is a C# union, so a `switch` must handle both cases, and the compiler checks that it does:

```csharp
string message = Email.Create(input) switch
{
    Email email => $"Hello, {email.Value}",
    Error error => $"{error.Code}: {error.Message}",
};
```

`Match(onSuccess, onFailure)` does the same as an expression, and `TryGetResultValue(out var email)` /
`TryGetError(out var error)` check and read in one step. Do not read `ResultValue` or `Error` after an `if
(result.IsSuccess)`: they throw on the wrong track, and analyzer RU0004 warns about it.

## 3. Validating several fields at once

Registration needs an email, a password and an age. A user should see every problem in one response, not one per try,
so the fields are validated together with `CombineAll`. `Password.Create` and `User.ParseAge` are built like
`Email.Create`, each with its own catalog (`password.too_short`, `user.invalid_age`, …):

```csharp
public sealed record RegisterUser(string? Email, string? Password, string? Age);

public static Result<User> Validate(RegisterUser command) =>
    Result.CombineAll(
            Email.Create(command.Email).MapError(e => e.WithField("email")),
            Password.Create(command.Password).MapError(e => e.WithField("password")),
            User.ParseAge(command.Age).MapError(e => e.WithField("age")))
        .Bind((email, password, age) => User.Create(email, password, age));
```

- `CombineAll` returns a tuple when every result succeeds, and a failure that groups **all** errors in `Details`
  otherwise. `Bind` on the tuple takes one parameter per value.
- `WithField` names the request field. The handler sets it, not `Email.Create`, because only the handler knows that
  the request calls it `email`.
- `Map` transforms a value (`T → U`); `Bind` continues with a step that can fail (`T → Result<U>`). Use `Bind` whenever
  the lambda returns a result; analyzer RU0003 catches `Map` used by mistake.

## 4. An async use case

Real use cases call a database. Every pipeline method has an `…Async` form that works on `Task<Result<T>>`, so the
whole use case is one expression with one `await` at the end:

```csharp
public sealed class UserService(IUserRepository users)
{
    public Task<Result<User>> RegisterAsync(RegisterUser command, CancellationToken ct) =>
        Validate(command)
            .Ensure(user => user.Age >= 18, User.Errors.TooYoung)
            .EnsureAsync(user => users.EnsureEmailIsFreeAsync(user.Email, ct))
            .TapAsync(user => users.AddAsync(user, ct));
}
```

Each step runs only if everything before it succeeded; the first failure travels to the end unchanged.

- `Ensure` turns a success into a failure when a rule does not hold, and keeps the value otherwise.
- `EnsureAsync` with a check that returns `Task<Result>` (here: "is the email free?") keeps the user and stops on the
  check's error.
- `TapAsync` runs a side effect (saving) and passes the value on.

The cancellation token is passed by closure; the delegates take no token.

## 5. An HTTP endpoint

`ResultUnion.AspNetCore` turns the result into a response: the success becomes a typed result, a failure becomes an
RFC 9457 problem response with the right status code.

```csharp
using ResultUnion.AspNetCore;

app.MapPost("/users", (RegisterUser command, UserService service, CancellationToken ct) =>
        service.RegisterAsync(command, ct)
            .MapAsync(UserResponse.From)
            .ToHttpResultAsync(user => TypedResults.Created($"/users/{user.Id}", user)))
    .ProducesErrors(ErrorType.Validation, ErrorType.Conflict, ErrorType.BusinessRule);
```

A request with an empty email and a password that is too short gets one `400 Bad Request` with both problems:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "detail": "One or more validation errors occurred.",
  "errors": {
    "email": [ "Email is required." ],
    "password": [ "Password must be at least 8 characters." ]
  },
  "code": "validation.failed",
  "details": [
    { "code": "email.required", "field": "email", "message": "Email is required." },
    { "code": "password.too_short", "field": "password", "message": "Password must be at least 8 characters.",
      "arguments": { "minimumLength": 8 } }
  ],
  "traceId": "00-40255ebae1df64bfe4404e483be78cda-2991eb1cb8e0c54e-02"
}
```

`errors` is what a form needs (messages per field), `details` what a client that translates needs (a code and
arguments per field), and `code` identifies the whole failure.

The success response is documented in OpenAPI from the return type; `ProducesErrors` lists the problem responses the
endpoint can return.

## Next steps

- [EF Core](ef-core.md): lookups, saving, and concurrency conflicts as results.
- [Pipeline behaviors](pipeline-behaviors.md): validation, authorization and transactions written once for every
  handler.
- [Testing](testing.md): asserting on results and errors.
- [Design decisions](../DESIGN.md): why the library is built this way.
