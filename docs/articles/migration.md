# Moving from FluentResults or ErrorOr

The three libraries answer the same question: an operation returns a value or a failure, and the caller has to look.
Moving to ResultUnion changes the code more than the ideas, and the codes your clients see can stay exactly as they
are. This page puts the same value objects and use case side by side, shows the names that change, and how old and new
code can live together while you move one feature at a time. Every piece of code on it compiles and runs in
`ResultUnion.Articles.Tests` (`MigrationArticle.cs`), with FluentResults 4.0 and ErrorOr 2.1, and a test checks that
the three versions return the same codes for the same input.

## Names

| FluentResults | ErrorOr | ResultUnion |
|---|---|---|
| `Result<T>`, `Result` | `ErrorOr<T>`, `ErrorOr<Success>` | `Result<T>`, `Result` |
| `Result.Ok(value)`, `Result.Fail(error)` | the value or the `Error`, converted implicitly | the value or the `Error`, converted implicitly |
| `IsSuccess` / `IsFailed`, `Value`, `Errors` | `IsError`, `Value`, `Errors`, `FirstError` | `switch`, `TryGetResultValue` / `TryGetError`, `Match` |
| `Bind`, `Map` | `Then` | `Bind` (the step can fail), `Map` (it cannot) |
| `Result.Merge` | a `List<Error>` | `Result.CombineAll`: one error, or one grouped error with the others as `Details` |
| `Error` with metadata | `Error.Validation(code, description)` and the other factories | `Error.Create(code, message, type)` in a catalog, values with `WithArgument` |
| `Result.Try` | | `Result.Try` / `Result.TryAsync` (never catch cancellation) |

Three differences change how code reads, not only what it is called:

- **One error, not a list.** A ResultUnion result fails with one `Error`. Several failures at once (every invalid
  field) are one grouped error whose `Details` hold them, which is what an HTTP client receives as `details` and
  `errors`. Code that loops over `Errors` reads `error.Details` instead, and the common case, one failure, needs no
  loop at all.
- **A code and a category on every error.** FluentResults errors have a message and optional metadata, so a code is a
  convention (`WithMetadata("code", ...)` below). ErrorOr has codes and a type, as ResultUnion does. In ResultUnion the
  code and the `ErrorType` are required, and each error is declared once in its concept's `Errors` catalog.
- **Async is `Task`.** Every operation has an `…Async` form on `Task<Result<T>>`, so a chain has one `await` at its
  end, and the `CancellationToken` goes into the delegates by closure.

## A value object

With FluentResults, the code travels as metadata:

```csharp
        public static Result<Email> Create(string? input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return Result.Fail(new Error("Email is required.").WithMetadata("code", "email.required"));

            var trimmed = input.Trim();
            return trimmed.Contains('@')
                ? Result.Ok(new Email(trimmed.ToLowerInvariant()))
                : Result.Fail(new Error($"'{trimmed}' is not a valid email.").WithMetadata("code", "email.invalid_format"));
        }
```

With ErrorOr, the factories take the code:

```csharp
        public static ErrorOr<Email> Create(string? input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return Error.Validation("email.required", "Email is required.");

            var trimmed = input.Trim();
            return trimmed.Contains('@')
                ? new Email(trimmed.ToLowerInvariant())
                : Error.Validation("email.invalid_format", $"'{trimmed}' is not a valid email.");
        }
```

With ResultUnion, the errors move into the type's catalog, where every code is declared once and the values a message
is made of are arguments (for clients that translate it, see [Translating error messages](localization.md)):

```csharp
    public static class Errors
    {
        public static readonly Error Required =
            Error.Create("email.required", "Email is required.", ErrorType.Validation);
```

```csharp
    public static Result<Email> Create(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return Errors.Required;

        var trimmed = input.Trim();
        return trimmed.Contains('@')
            ? new Email(trimmed.ToLowerInvariant())
            : Errors.InvalidFormat(trimmed);
    }
```

The method keeps its shape; the strings leave it. If the old code has the same message in several places, the catalog
is where they meet.

## Every invalid input at once

FluentResults merges the results and converts the failure:

```csharp
            var emailResult = Email.Create(email);
            var passwordResult = Password.Create(password);
            var merged = Result.Merge(emailResult, passwordResult);  // every failure, as a list of errors
            if (merged.IsFailed)
                return merged.ToResult<Account>();

            return Result.Ok(new Account(emailResult.Value, passwordResult.Value));
```

ErrorOr collects the errors by hand:

```csharp
            var emailResult = Email.Create(email);
            var passwordResult = Password.Create(password);
            List<Error> errors = [.. emailResult.ErrorsOrEmptyList, .. passwordResult.ErrorsOrEmptyList];
            if (errors.Count > 0)
                return errors;

            return new Account(emailResult.Value, passwordResult.Value);
```

ResultUnion's `CombineAll` takes the results and gives the values to the next step, one parameter each, or every
failure as one grouped error; `WithField` names each input the way the request does:

```csharp
        Result.CombineAll(
                Email.Create(command.Email).MapError(e => e.WithField("email")),
                Password.Create(command.Password).MapError(e => e.WithField("password")),
                User.ParseAge(command.Age).MapError(e => e.WithField("age")))
            .Bind((email, password, age) => User.Create(email, password, age));
```

No `Value` is read before it is known to be there, so there is nothing to forget. The analyzers report the cases that
remain, such as `ResultValue` read without a check (RU0004) or `Map` with a step that can fail (RU0003).

## At the endpoint

With FluentResults or ErrorOr, the mapping from a failure to an HTTP response is the project's own code, usually a
`switch` on the error type that builds a `ProblemDetails`. ResultUnion.AspNetCore brings that mapping: `ToHttpResult`
writes the value with the success status you choose, or an RFC 9457 problem with the status of the error's category,
its `code`, its arguments and, for a grouped error, every entry with its field:

```csharp
        app.MapPost("/users", (RegisterUser command, UserService service, CancellationToken ct) =>
```

```csharp
                    .ToHttpResultAsync(user => TypedResults.Created($"/users/{user.Id}", user)))
```

A client that used to parse your problem responses by hand can keep doing so: the codes are the same strings. A .NET
client can read them back as an `Error` with ResultUnion.Http (`ReadResultAsync`).

## One feature at a time

The codes are the contract, so the old and the new code can run side by side while you move a feature at a time.
Where new code calls code that is not migrated yet, an adapter turns the old result into a ResultUnion one. One error
stays one error; several become one grouped error, as `CombineAll` would report them:

```csharp
        public static Result<T> ToResultUnion<T>(this EO.ErrorOr<T> result) where T : notnull =>
            result.IsError ? Group(result.Errors.Select(FromErrorOr).ToList()) : result.Value;
```

```csharp
        private static Error FromErrorOr(EO.Error error) =>
            Error.Create(error.Code, error.Description, error.Type switch
            {
                EO.ErrorType.Validation => ErrorType.Validation,
                EO.ErrorType.NotFound => ErrorType.NotFound,
                EO.ErrorType.Conflict => ErrorType.Conflict,
                EO.ErrorType.Unauthorized => ErrorType.Unauthorized,
                EO.ErrorType.Forbidden => ErrorType.Forbidden,
                EO.ErrorType.Failure => ErrorType.BusinessRule,  // a failure the caller can understand
                _ => ErrorType.Unexpected,
            });
```

```csharp
        private static Error Group(List<Error> errors) => errors.Count == 1 ? errors[0] : Error.Validation(errors);
```

ErrorOr's `Failure` has no direct counterpart: it is a failure the caller is meant to understand, which is closest to
`BusinessRule` (422). Choose per project, and keep the choice in this one place. FluentResults has no category, so its
adapter takes one from the caller:

```csharp
        public static Result<T> ToResultUnion<T>(this FR.Result<T> result, ErrorType type) where T : notnull =>
            result.IsSuccess ? result.Value : Group(result.Errors.Select(error => FromFluentResults(error, type)).ToList());
```

```csharp
        private static Error FromFluentResults(FR.IError error, ErrorType type) =>
            Error.Create(error.Metadata.TryGetValue("code", out var code) && code is string text ? text : "legacy.error", error.Message, type);
```

A test per feature pins the contract during the move: the same input gives the same codes before and after.

```csharp
        public void Every_version_gives_the_same_codes(string? email, string? password)
        {
            var expected = Codes(Result.CombineAll(Email.Create(email), Password.Create(password)).Map((e, p) => (e, p)));

            Assert.Equal(expected, Codes(FluentAccount.Register(email, password).ToResultUnion(ErrorType.Validation)));
            Assert.Equal(expected, Codes(ErrorOrAccount.Register(email, password).ToResultUnion()));
        }
```

When the last caller of a feature is migrated, its adapter calls go, and so does the old library once no feature uses
it.

## Coming from OneOf

`OneOf<User, NotFound, ValidationFailed>` lists every outcome in the signature, and the caller matches on each. A
ResultUnion result has two cases, the value and an `Error`, and the outcomes become error categories and codes:
`NotFound` is `ErrorType.NotFound` with a catalog code such as `user.not_found`, a validation failure is
`ErrorType.Validation`. The signature then says less (an `Error`, not which ones), and in return every operation
composes with every other one in a pipeline, and HTTP and gRPC know how to send each category. A method that needs
callers to handle a specific outcome differently matches on its code: `error.Code == User.Errors.NotFound(id).Code`.
