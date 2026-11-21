# ResultUnion

A small implementation of the **Result pattern** for .NET, built on **C# 15 unions**.
An operation returns `Result<T>`: either the value or an `Error`. Failures are part of the
signature instead of exceptions, and pipelines run only while every step succeeds.

```csharp
public Result<User> Register(string? email, string? age) =>
    Email.Create(email)                                   // Result<Email>
        .Ensure(repository.EnsureEmailIsFree)             // keep the email, fail on conflict
        .Bind(e => User.ParseAge(age).Map(a => new User(Guid.NewGuid(), e, a)))
        .Ensure(u => u.Age >= 18, u => Error.BusinessRule($"{u.Email} is {u.Age}."))
        .Tap(repository.Add);

var message = Register("ann@example.com", "34") switch
{
    User user   => $"registered {user.Email}",
    Error error => $"error {error.Code}: {error.Message}",
};
```

## Requirements

- .NET 11 SDK (`net11.0`)
- C# 15 for unions (`[Union]`, `IUnion`) and C# 14 extension members, both default for `net11.0`

## Solution layout

| Project | Contents |
|---|---|
| `ResultUnion` | The library: `Result<T>`, `Result`, `Error`, `ErrorType` and the async extensions in `Async/`. No dependencies. |
| `ResultUnion.AspNetCore` | ASP.NET Core integration: status codes, RFC 9457 problem details, `ToHttpResult` for Minimal APIs |
| `ResultUnion.Tests` | xUnit v3 tests for `ResultUnion`; no ASP.NET Core dependency |
| `ResultUnion.AspNetCore.Tests` | xUnit v3 tests for `ResultUnion.AspNetCore`, including the JSON a client receives |
| `ResultUnion.Showcase` | A console app that demonstrates the API on a small user-registration domain |

```bash
dotnet build ResultLabIUnion.slnx
```

```bash
dotnet test ResultLabIUnion.slnx
```

```bash
dotnet run --project ResultUnion.Showcase
```

`global.json` opts `dotnet test` into Microsoft.Testing.Platform, which the .NET 10+ SDK requires for xUnit v3.

## The types

| Type | Meaning |
|---|---|
| `Result<T>` | A union of two cases: a `T` value (success) or an `Error` (failure). `T` cannot be `null`. |
| `Result` | The outcome of an operation without a value: success, or an `Error`. Not a union. |
| `Error` | `Code`, `Message`, `Type` (`ErrorType`), an optional `Field` and nested `Details`. Compared by value. |
| `IResultState`, `IResultState<T>` | Common interfaces, so results of different types can be handled together. |

A `default` result is never a half-initialised state: it is a failure carrying `Error.DefaultResult`.

### Creating results

```csharp
Result<int> a = Result<int>.Success(42);
Result<int> b = Result<int>.Failure(Error.NotFound("Order 7 was not found."));
Result<string> c = Result.Success("hi");           // type inferred

Result<int> d = 7;                                 // union conversion from a case
Result<int> e = Error.Validation("Bad input.");
Result f = Error.Forbidden("Admins only.");        // user-defined conversion on Result
```

### Errors

Factories set a stable code and a category:

| Factory | Code | `ErrorType` | Typical HTTP status |
|---|---|---|---|
| `Error.Validation` | `validation.failed` | `Validation` | 400 |
| `Error.Unauthorized` | `unauthorized` | `Unauthorized` | 401 |
| `Error.Forbidden` | `forbidden` | `Forbidden` | 403 |
| `Error.NotFound` | `resource.not_found` | `NotFound` | 404 |
| `Error.Conflict` | `conflict` | `Conflict` | 409 |
| `Error.Concurrency` | `concurrency.conflict` | `Concurrency` | 409 / 412 |
| `Error.BusinessRule` | `business_rule.violated` | `BusinessRule` | 422 |
| `Error.Unexpected` | `unexpected` | `Unexpected` | 500 |
| `Error.Unavailable` | `dependency.unavailable` | `Unavailable` | 503 |
| `Error.Create(code, message, type)` | your own | any | |

Name the input an error is about with `WithField`, and group several errors with `Error.Validation(details)`:

```csharp
var error = Error.Validation("Email is required.").WithField("email");
var group = Error.Validation([error, Error.Validation("Too short.").WithField("password")]);
// group.Details: both errors, in order
```

Attach the values behind a message with `WithArgument`, so a client can show a translated message for the `code`
without parsing the English text:

```csharp
Error.Create("user.too_young", $"Users must be at least 18; {email} is {age}.", ErrorType.BusinessRule)
    .WithArgument("minimumAge", 18)
    .WithArgument("age", age);
// error.Arguments: { minimumAge: 18, age: 15 }
// template "მომხმარებელი უნდა იყოს მინიმუმ {minimumAge} წლის, არის {age}." → filled from the arguments
```

`Arguments` is read-only, survives `WithField`, `WithFieldPrefix` and grouping, and takes part in equality. Keep the
values simple (numbers, strings, dates) so they serialize. The library does no localization itself.

The library does not map errors to HTTP itself; `ErrorType` is meant for that mapping in your API layer.

## Using results

### Pattern matching

`Result<T>` is a union, so `switch` and `is` work on its cases, and the compiler checks that both are handled.
Matching does not box value types.

```csharp
string Describe(Result<User> result) => result switch
{
    User { Age: >= 65 } user           => $"senior {user.Email}",
    User user                          => $"user {user.Email}",
    Error { Type: ErrorType.NotFound } => "not found",
    Error error                        => $"error {error.Code}",
};
```

### Pipelines

| Method | Runs on | Does |
|---|---|---|
| `Map(v => …)` | success | transforms the value |
| `Bind(v => Result…)` | success | chains a step that can fail (`Result<U>` or `Result`) |
| `Ensure(predicate, error)` | success | fails when the predicate is false; also `Ensure(predicate, v => error)` and `Ensure(v => Result)` |
| `MapError(e => …)` | failure | transforms the error |
| `Tap(v => …)` / `TapError(e => …)` | success / failure | side effect, returns the same result |
| `Match(onSuccess, onFailure)` | both | produces a value, or runs an action |

`Result` (no value) has the same operations, with parameterless success delegates.

### Reading the value or the error

```csharp
if (result.TryGetResultValue(out var user)) { /* … */ }
else if (result.TryGetError(out var error)) { /* … */ }

int age = ageResult.GetValueOrDefault(-1);
int value = ageResult.ResultValue;   // throws InvalidOperationException on a failure
Error err = ageResult.Error;         // throws InvalidOperationException on a success
```

Prefer `switch`, `Match` or the `TryGet…` methods. `GetValueOrDefault()` without an argument returns `0`
for a failed `Result<int>`, which is easy to mistake for a real value.

### Combining independent checks

```csharp
Result.Combine(a, b, c);      // stops at the first failure
Result.CombineAll(a, b, c);   // reports every failure

Result form = Result.CombineAll(
[
    Email.Create(email).MapError(e => e.WithField("email")),
    User.ParseAge(age).MapError(e => e.WithField("age")),
]);
// several failures: Error.Validation(details), one detail per field
// one failure: returned unchanged
```

Both have an overload for `IEnumerable<IResultState>`, which accepts `Result<T>` of different types (each one is boxed).

To keep the values, pass two to four `Result<T>` arguments: `CombineAll` then returns a tuple on success, and
reports every error on failure. `Map` and `Bind` on a tuple result take one parameter per value, so a factory can
be passed as a method group:

```csharp
Result<User> user = Result.CombineAll(
        Email.Create(cmd.Email).MapError(e => e.WithField("email")),
        Password.Create(cmd.Password).MapError(e => e.WithField("password")))
    .Bind(User.Create);                       // User.Create(Email, Password) -> Result<User>
```

Mixing `Result` and `Result<T>` arguments is not supported by these overloads; call `ToResult()` on the generic ones
and use the plain `CombineAll`, or use the `IEnumerable<IResultState>` overload.

### Collections

For a list in a command, such as order lines, `CombineAll` on a sequence returns every value or every error.
With `field`, each error's path gets the element index in front: `quantity` becomes `lines[2].quantity`.

```csharp
Result<IReadOnlyList<OrderLine>> lines = command.Lines.CombineAll(OrderLine.Create, field: "lines");

Result<IReadOnlyList<int>> numbers = results.CombineAll();            // IEnumerable<Result<int>>

Result<IReadOnlyList<Product>> products =
    await command.ProductIds.CombineAllAsync(id => repository.FindAsync(id, ct), field: "productIds");
```

- Every element is processed, even after a failure, so every error is reported; the sequence is enumerated once.
- `CombineAllAsync` runs the elements **one after another**, never concurrently, so it is safe with an EF Core `DbContext`.
- An element can report several errors (a group); `error.Flatten()` returns the innermost errors, one per invalid input,
  each with its full path (`lines[1].product`, `lines[1].quantity`). `ToProblemDetails()` uses it, so the HTTP response
  lists them the same way.
- `Error.WithFieldPrefix(prefix)` builds such paths yourself.

### Exceptions and `null`

At the boundary with code that throws or returns `null`:

```csharp
Result<Uri> uri = Result.Try(() => new Uri(input), ex => Error.Validation(ex.Message));

Result<User> user = Result.FromNullable(cache.GetValueOrDefault(id), Error.NotFound("No such user."));

Task<Result<User>> found = db.Users.FirstOrDefaultAsync(u => u.Id == id)
    .ToResultAsync(Error.NotFound("No such user."));
```

- `Try` / `TryAsync` never catch `OperationCanceledException`: cancellation is not a business failure.
- The error mapper is required; nothing is turned into `Unexpected` silently.
- `ToResultAsync` turns only `null` into an error; an exception from the lookup itself is not caught.

## Async

Every operation has an `…Async` counterpart, in two groups:

- on `Result<T>` / `Result`, taking an async delegate, for an async step in the middle of a pipeline;
- on `Task<Result<T>>` / `Task<Result>`, taking a sync or an async delegate, so the chain continues after the first async step.

```csharp
var message = await Email.Create(input)
    .EnsureAsync(email => repository.EnsureEmailIsFreeAsync(email, ct))   // Task<Result<Email>>
    .BindAsync(email => User.ParseAge(age).Map(a => new User(Guid.NewGuid(), email, a)))
    .TapAsync(user => repository.AddAsync(user, ct))
    .MatchAsync(user => $"registered {user.Email}", error => error.Code);
```

- **`Task`, not `ValueTask`.** Most async APIs (EF Core, `HttpClient`, ASP.NET Core) return `Task`, and having
  both would make async lambdas ambiguous. Use `.AsTask()` for a `ValueTask` source.
- **Cancellation by closure.** Delegates do not take a `CancellationToken`; capture it: `user => orders.CreateAsync(user, ct)`.
- **Eager validation.** A `null` delegate or source throws when the method is called, not when the task is awaited.
  Exceptions thrown by your delegates surface through the returned task.
- The library uses `ConfigureAwait(false)` throughout.

## Generic code: pipeline behaviors

`Result` and `Result<T>` implement `IResultFactory<TSelf>`, so code that only knows the result type as a type
parameter can still create a failure. This is what a CQRS pipeline behavior (validation, authorisation,
transactions) needs when it wraps handlers that return `Result`, `Result<OrderId>`, `Result<UserDto>` and so on:

```csharp
public static async Task<TResponse> RequireRoleAsync<TResponse>(string role, Func<Task<TResponse>> handler)
    where TResponse : IResultFactory<TResponse>
{
    if (role != "admin")
        return TResponse.Failure(Error.Forbidden("Admins only."));   // the handler does not run

    var response = await handler();
    // IResultFactory<T> is also an IResultState: response.IsSuccess / response.Error are available here.
    return response;
}
```

There is no generic `Success`: a successful `Result<T>` needs a value that the behavior does not have.

## ASP.NET Core: `ResultUnion.AspNetCore`

A separate project, so the core library and your domain stay free of ASP.NET Core.

```csharp
using ResultUnion.AspNetCore;

app.MapPost("/users", (RegisterUser command, RegistrationService service) =>
    service.Register(command.Email, command.Age)
        .ToHttpResult(user => TypedResults.Created($"/users/{user.Id}", user)));

app.MapDelete("/users/{email}", (string email, AsyncRegistrationService service, CancellationToken ct) =>
    service.DeactivateAsync(email, ct).ToHttpResultAsync());    // 204 No Content, or a problem
```

| Call | Success | Failure |
|---|---|---|
| `Result<T>.ToHttpResult()` | `200 OK` with the value | problem response |
| `Result<T>.ToHttpResult(value => …)` | the response you build | problem response |
| `Result.ToHttpResult()` | `204 No Content` | problem response |
| `Task<Result…>.ToHttpResultAsync(…)` | same, after `await` | same |

A failure becomes an RFC 9457 `application/problem+json` response:

- the status comes from `ErrorType`: Validation 400, Unauthorized 401, Forbidden 403, NotFound 404,
  Conflict and Concurrency 409, BusinessRule 422, Unavailable 503, Unexpected 500 (`ErrorType.ToStatusCode()`);
- `detail` is the message, and the stable error `code` is an extension, so clients can switch on it;
- `Arguments` become the `arguments` extension, and each entry of `details` carries its own (omitted when empty);
- an error with a `Field` or `Details` becomes a validation problem: `errors` lists the messages per field, and
  `details` lists every error with its `code`, `field` and `message`;
- `Unexpected` errors never send their message or arguments to the client (they often come from an exception); log them instead.

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "detail": "One or more validation errors occurred.",
  "errors": { "email": ["'dan@' is not a valid email."], "password": ["Password must be at least 8 characters."] },
  "code": "validation.failed",
  "details": [
    { "code": "email.invalid_format", "field": "email", "message": "'dan@' is not a valid email." },
    { "code": "password.too_short", "field": "password", "message": "Password must be at least 8 characters." }
  ]
}
```

`Error.ToProblemDetails()` gives the `ProblemDetails` object itself, for controllers or exception handlers.

## Things to know

- **Do not pass `async` lambdas to `Result.Try`.** They compile, but exceptions after the first `await` are not
  observed. Use `Result.TryAsync`.
- **Do not serialize results directly.** `System.Text.Json` writes only the case of a `Result<T>` (the value, or the
  error object), and throws for a successful `Result` because its `Error` getter throws. Map results to a DTO or a
  ProblemDetails response at the API boundary.
- **`Result<Error>` is not supported.** Both cases would have the type `Error`, so conversions are ambiguous (CS0457).
- **Union members are hidden from IntelliSense.** `Value`, `HasValue` and the two `TryGetValue` overloads exist for
  the compiler's pattern matching. Use `TryGetResultValue` and `TryGetError`; `TryGetValue(out var x)` does not compile
  because the two overloads make `out var` ambiguous.
- **Other languages.** The `T` / `Error` → `Result<T>` conversion is a C# 15 union conversion. VB, F# and projects with
  `LangVersion` below 15 use `Result<T>.Success` and `Result<T>.Failure`.
- **Equality.** Results and errors compare by value. One quirk: `default(Result<int>)` is not equal to
  `Result<int>.Failure(Error.DefaultResult)`, although both carry the same error.
