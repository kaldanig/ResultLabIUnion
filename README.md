# ResultUnion

[![ResultUnion](https://img.shields.io/nuget/v/ResultUnion.svg?label=ResultUnion)](https://www.nuget.org/packages/ResultUnion)
[![ResultUnion.AspNetCore](https://img.shields.io/nuget/v/ResultUnion.AspNetCore.svg?label=ResultUnion.AspNetCore)](https://www.nuget.org/packages/ResultUnion.AspNetCore)
[![ResultUnion.Grpc](https://img.shields.io/nuget/v/ResultUnion.Grpc.svg?label=ResultUnion.Grpc)](https://www.nuget.org/packages/ResultUnion.Grpc)
[![ResultUnion.FluentValidation](https://img.shields.io/nuget/v/ResultUnion.FluentValidation.svg?label=ResultUnion.FluentValidation)](https://www.nuget.org/packages/ResultUnion.FluentValidation)
[![CI](https://github.com/kaldanig/ResultLabIUnion/actions/workflows/ci.yml/badge.svg)](https://github.com/kaldanig/ResultLabIUnion/actions/workflows/ci.yml)
[![Docs](https://img.shields.io/badge/docs-kaldanig.github.io-blue)](https://kaldanig.github.io/ResultLabIUnion/)

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

Why it is built this way, with the alternatives that were considered: [docs/DESIGN.md](docs/DESIGN.md).

## Requirements

- .NET 11 SDK (`net11.0`)
- C# 15 for unions (`[Union]`, `IUnion`) and C# 14 extension members, both default for `net11.0`
- The libraries are compiled with .NET 11 Runtime Async (`<Features>runtime-async=on</Features>`, set for every
  `net11.0` project in `Directory.Build.targets`); `-p:EnableRuntimeAsync=false` builds the classic state machines

## Solution layout

| Project | Contents |
|---|---|
| `ResultUnion` | The library: `Result<T>`, `Result`, `Error`, `ErrorType` and the async extensions in `Async/`. No dependencies. |
| `ResultUnion.AspNetCore` | ASP.NET Core integration: status codes, RFC 9457 problem details, `ToHttpResult` for Minimal APIs |
| `ResultUnion.Grpc` | gRPC integration: status codes, the `google.rpc` rich error model, `GetValueOrThrowRpc` for services, `ToError` for clients |
| `ResultUnion.FluentValidation` | FluentValidation integration: a `ValidationResult` as a `Result` with one field error per failure, `ValidateToResultAsync` |
| `ResultUnion.Http` | HTTP client integration: a response read back as a `Result`, a problem details body as an `Error` (the client side of `ResultUnion.AspNetCore`) |
| `ResultUnion.Testing` | Test assertions for any test framework: `ShouldSucceed()`, `ShouldFail("code")`, `ShouldHaveFieldErrors(...)` |
| `ResultUnion.Templates` | `dotnet new resultunion-api` and `resultunion-grpc`: a Minimal API or a gRPC service with one feature that follows the conventions, and its tests |
| `ResultUnion.Analyzers` | Roslyn analyzers shipped inside the `ResultUnion` package: a discarded `Result`, `async` code in `Result.Try`, `Map` that should be `Bind` ([docs/ANALYZERS.md](docs/ANALYZERS.md)) |
| `ResultUnion.CodeFixes` | The code fixes for those rules (`_ =`, `TryAsync`, `Bind`, a `when` filter for cancellation, `Bind` with a failure, `ToHttpResult`), in a separate assembly because they need Roslyn's Workspaces; shipped next to the analyzers |
| `ResultUnion.Tests` | xUnit v3 tests for `ResultUnion`; no ASP.NET Core dependency |
| `ResultUnion.AspNetCore.Tests` | xUnit v3 tests for `ResultUnion.AspNetCore`, including the JSON a client receives |
| `ResultUnion.Grpc.Tests` | xUnit v3 tests for `ResultUnion.Grpc`, including server-to-client round trips |
| `ResultUnion.FluentValidation.Tests` | xUnit v3 tests for `ResultUnion.FluentValidation`, including the JSON round trip of the errors it creates |
| `ResultUnion.Http.Tests` | xUnit v3 tests for `ResultUnion.Http`, including round trips through a real ASP.NET Core server and responses from other servers |
| `ResultUnion.Testing.Tests` | xUnit v3 tests for `ResultUnion.Testing`, including the exact failure messages |
| `ResultUnion.Analyzers.Tests` | Tests that compile snippets against the real library and check each rule's diagnostics |
| `ResultUnion.Benchmarks` | BenchmarkDotNet measurements of matching, pipelines, failures against exceptions, `CombineAll` and async chains ([docs/BENCHMARKS.md](docs/BENCHMARKS.md)) |
| `ResultUnion.Samples.Domain` | The sample domain shared by the demos: `Email`, `Password`, `User`, `OrderLine`, `Order` with their error catalogs, and a Georgian `ErrorLocalizer`. References only `ResultUnion`. |
| `ResultUnion.Samples.Application` | The sample application layer shared by the Web API and the gRPC service: commands, use cases, in-memory storage. No HTTP, no gRPC. |
| `ResultUnion.Articles` | The code of the articles in `docs/articles`, as a project that compiles; `ResultUnion.Articles.Tests` runs the testing article's samples and checks that every C# line of every article appears in one of the two |
| `ResultUnion.Showcase` | A console app that demonstrates the API section by section |
| `ResultUnion.WebApi` | A Minimal API demo with a browser UI, OpenAPI and a Scalar reference: real requests, real problem responses, and OpenTelemetry for the Aspire Dashboard |
| `ResultUnion.WebApi.Tests` | Integration tests for the Web API: the real app in memory (`WebApplicationFactory`), real HTTP requests, the problem JSON a client receives, the Georgian translation and the OpenAPI document |
| `ResultUnion.GrpcService` | The same use cases as a gRPC service (`Protos/demo.proto`), failures as rich gRPC statuses |
| `ResultUnion.GrpcClient` | A console client for the gRPC service that turns every `RpcException` back into an `Error` |
| `ResultUnion.AppHost` | Runs the Web API, the gRPC service and the client together under Aspire, with their telemetry in Aspire's dashboard |

```bash
dotnet build ResultLabIUnion.slnx
```

```bash
dotnet test ResultLabIUnion.slnx
```

```bash
dotnet run --project ResultUnion.Showcase
```

```bash
dotnet run --project ResultUnion.WebApi
```

```bash
dotnet run --project ResultUnion.GrpcService
```

```bash
dotnet run --project ResultUnion.GrpcClient
```

`global.json` opts `dotnet test` into Microsoft.Testing.Platform, which the .NET 10+ SDK requires for xUnit v3.

### The Web API demo

`dotnet run --project ResultUnion.WebApi` serves the API on <http://localhost:5258>:

- **`/`**: a small browser UI (`wwwroot`, plain HTML and JavaScript, no build step) with a form for every scenario.
  It puts each field error under its input, including the lines of an order (`lines[1].quantity`), switches the
  server messages between English and Georgian, locks the reset button for `retryAfterSeconds` after a 429, and shows
  the raw request and problem response next to the forms;
- **`/scalar`**: the Scalar API reference (the OpenAPI document is at `/openapi/v1.json`);
- `ResultUnion.WebApi.http` has a request for every scenario.

| Endpoint | Shows | Responses |
|---|---|---|
| `POST /users` | `CombineAll` over email, password and age; a domain rule; a duplicate check that also catches a race | 201, 400, 409, 422 |
| `GET /users/{email}` | `ToResultAsync` on a nullable lookup | 200, 400, 404 |
| `DELETE /users/{email}` | a `Result` without a value; a second call is a conflict | 204, 400, 404, 409 |
| `POST /users/{email}/password-reset` | a quota: the fourth call within a minute is 429 with `arguments.retryAfterSeconds` | 202, 400, 404, 429 |
| `POST /orders` | collection validation (`lines[1].quantity`) and an aggregate factory | 201, 400, 404 |
| `GET /admin/users` | a generic `IResultFactory` behavior; send `X-Role: admin` | 200, 403 |

Send `Accept-Language: ka` to get Georgian messages: an endpoint filter translates each problem from its `code` and
`arguments`, without reading the English text. Every response is documented in OpenAPI from the typed results and
`.ProducesErrors(…)`; no endpoint declares its responses by hand. Storage is in memory, with `ann@example.com`
registered at startup.

### The gRPC demo

`ResultUnion.GrpcService` runs the same use cases (`ResultUnion.Samples.Application`) behind `Protos/demo.proto`, on
<http://localhost:5259> (HTTP/2 only, with gRPC reflection for grpcurl or Postman). Each method maps the request to a
command and ends with `GetValueOrThrowRpcAsync`. Start it, then run `ResultUnion.GrpcClient` in another terminal:
every call goes through `Result.TryAsync(…, (RpcException ex) => ex.ToError())`, and the client prints what came back.

```text
RegisterUser (too young)  ->  FailedPrecondition (BusinessRule)
    user.too_young: Users must be at least 18; kid@example.com is 15.
    arguments: minimumAge=18, age=15

PlaceOrder (invalid lines)  ->  InvalidArgument (Validation)
    lines[1].product     order_line.product_required: Product is required.
    lines[1].quantity    order_line.quantity_not_positive: Quantity must be positive, was 0.

RequestPasswordReset #4  ->  ResourceExhausted (TooManyRequests, transient)
```

The Web API and the gRPC service share the domain and the application layer; they differ only in how they map their
contract to a command and the result to their protocol. The role for `ListUsers` comes from the call metadata
`x-role: admin`.

## The types

| Type | Meaning |
|---|---|
| `Result<T>` | A union of two cases: a `T` value (success) or an `Error` (failure). `T` cannot be `null`. |
| `Result` | The outcome of an operation without a value: success, or an `Error`. Not a union. |
| `Error` | `Code`, `Message`, `Type` (`ErrorType`), an optional `Field` and nested `Details`. Compared by value. |
| `IAnyResult` | Any result, whatever its value type, so results of different types can be handled together. |

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
| `Error.TooManyRequests` | `rate_limit.exceeded` | `TooManyRequests` | 429 |
| `Error.Unexpected` | `unexpected` | `Unexpected` | 500 |
| `Error.Unavailable` | `dependency.unavailable` | `Unavailable` | 503 |
| `Error.Create(code, message, type)` | your own | any | |

A category says what the caller should *do* (fix the input, sign in, wait and retry, reload); the specific reason is
the `Code`. `IsTransient` tells whether running the operation again may succeed: true for `Unavailable`,
`TooManyRequests` and `Concurrency` (retry the whole operation, so it reads fresh data first).

```csharp
if (result.TryGetError(out var error) && error.IsTransient) { /* retry with backoff */ }
```

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

Both have an overload for `IEnumerable<IAnyResult>`, which accepts `Result<T>` of different types (each one is boxed).

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
and use the plain `CombineAll`, or use the `IEnumerable<IAnyResult>` overload.

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
- To translate **one exception type only**, type the mapper's parameter; every other exception propagates:
  `Result.Try(() => int.Parse(text), (FormatException ex) => Error.Validation(ex.Message))`.
  An untyped mapper (`ex => …`) catches every exception.
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

- **`Task` delegates and results; `Task` or `ValueTask` sources.** Most async APIs (`HttpClient`, ASP.NET Core) return
  `Task`, and accepting both kinds of delegate would make async lambdas ambiguous, so delegates and return values are
  `Task`. A chain can still start from a `ValueTask`: `ValueTask<T?>.ToResultAsync` (EF Core's `FindAsync`) and the
  whole pipeline on `ValueTask<Result<T>>` / `ValueTask<Result>` await the source once and continue on `Task`:
  `await db.Users.FindAsync([id], ct).ToResultAsync(User.Errors.NotFound(id))`.
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
    // IResultFactory<T> is also an IAnyResult: response.IsSuccess / response.Error are available here.
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
            .ToHttpResult(user => TypedResults.Created($"/users/{user.Id}", user)))
    .ProducesErrors(ErrorType.Validation, ErrorType.Conflict, ErrorType.BusinessRule);

app.MapDelete("/users/{email}", (string email, AsyncRegistrationService service, CancellationToken ct) =>
        service.DeactivateAsync(email, ct).ToHttpResultAsync())    // 204 No Content, or a problem
    .ProducesErrors(ErrorType.NotFound, ErrorType.Conflict);
```

| Call | Returns | Success | Failure |
|---|---|---|---|
| `Result<T>.ToHttpResult()` | `Results<Ok<T>, ProblemHttpResult>` | `200 OK` with the value | problem |
| `Result<T>.ToHttpResult(v => TypedResults.Created(…))` | `Results<Created<T>, ProblemHttpResult>` | the response you build | problem |
| `Result.ToHttpResult()` | `Results<NoContent, ProblemHttpResult>` | `204 No Content` | problem |
| `Error.ToHttpResult()` | `ProblemHttpResult` | | problem |
| `Task<Result…>.ToHttpResultAsync(…)` | `Task<…>` of the same | same, after `await` | same |

**OpenAPI.** The responses are typed, so OpenAPI / Swagger documents the success response of each endpoint by itself.
A problem's status is only known at run time, so declare the categories an endpoint can fail with:
`.ProducesErrors(ErrorType.Validation, ErrorType.NotFound)` documents 400 (as a validation problem) and 404, using the
same mapping as the responses. It works on endpoints and on route groups. When the success can be one of several
responses (for example `Ok` or `Redirect`), return `IResult` from the lambda; that overload is untyped, so add
`.Produces<T>()` yourself.

```text
POST /users             201 UserResponse, 400 HttpValidationProblemDetails, 409 ProblemDetails, 422 ProblemDetails
DELETE /users/{email}   204, 404 ProblemDetails, 409 ProblemDetails
```

A failure becomes an RFC 9457 `application/problem+json` response:

- the status comes from `ErrorType`: Validation 400, Unauthorized 401, Forbidden 403, NotFound 404,
  Conflict and Concurrency 409, BusinessRule 422, TooManyRequests 429, Unavailable 503, Unexpected 500
  (`ErrorType.ToHttpStatusCode()`);
- `detail` is the message, and the stable error `code` is an extension, so clients can switch on it;
- `category` is the `ErrorType` name, which the status alone does not always give (Conflict and Concurrency are both 409);
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
  "category": "Validation",
  "details": [
    { "code": "email.invalid_format", "field": "email", "message": "'dan@' is not a valid email." },
    { "code": "password.too_short", "field": "password", "message": "Password must be at least 8 characters." }
  ]
}
```

`Error.ToProblemDetails()` gives the `ProblemDetails` object itself, for controllers or exception handlers.

## gRPC: `ResultUnion.Grpc`

The domain and application layers stay the same; only the boundary changes. A gRPC method returns the reply message,
and gRPC reports failures through the call's status, so the service ends the pipeline with `GetValueOrThrowRpc` (or
`ToReplyOrThrowRpc` for a result without a value):

```csharp
using ResultUnion.Grpc;

public override Task<UserReply> GetUser(GetUserRequest request, ServerCallContext context) =>
    users.FindAsync(request.Email, context.CancellationToken)      // Task<Result<User>>, as in the Web API
        .MapAsync(UserReply.From)
        .GetValueOrThrowRpcAsync();                                 // the reply, or RpcException with a rich status

public override Task<Empty> DeactivateUser(DeactivateUserRequest request, ServerCallContext context) =>
    users.DeactivateAsync(request.Email, context.CancellationToken)
        .ToReplyOrThrowRpcAsync(() => new Empty());
```

The `RpcException` carries gRPC's rich error model (`google.rpc.Status`):

| `Error` | gRPC |
|---|---|
| `Type` | status code: Validation → InvalidArgument, Unauthorized → Unauthenticated, Forbidden → PermissionDenied, NotFound → NotFound, Conflict and BusinessRule → FailedPrecondition, Concurrency → Aborted, TooManyRequests → ResourceExhausted, Unavailable → Unavailable, Unexpected → Internal |
| `Code` | `ErrorInfo.reason` in UPPER_SNAKE_CASE (`USER_TOO_YOUNG`), and the original in `ErrorInfo.metadata["code"]` |
| `Arguments` | `ErrorInfo.metadata`, as strings |
| `Field` / `Details` | `BadRequest.field_violations`, one per invalid input (`lines[1].quantity`) |
| `TooManyRequests` + `retryAfterSeconds` | `RetryInfo.retry_delay` |
| `Message` | the status message; `Unexpected` sends a fixed message and no arguments |

On the client, `ToError` turns the exception back into an `Error`, so a service calling another keeps working with
results. With the typed `TryAsync`, only `RpcException` is translated:

```csharp
Result<UserReply> user = await Result.TryAsync(
    () => client.GetUserAsync(request).ResponseAsync,
    (RpcException ex) => ex.ToError());
```

From a ResultUnion service, the type, code, message, arguments and field violations come back; argument values arrive
as strings, and the arguments of individual field violations are not transmitted. From any other server, the error
code is `grpc.<status>` (e.g. `grpc.deadline_exceeded`) with the type mapped from the status code.

## FluentValidation: `ResultUnion.FluentValidation`

A FluentValidation validator's outcome becomes a `Result`, with one field error per failure, so validators written
with FluentValidation plug into pipelines and reach HTTP and gRPC clients as field errors:

```csharp
using ResultUnion.FluentValidation;

public sealed class RegisterUserValidator : AbstractValidator<RegisterUser>
{
    public RegisterUserValidator()
    {
        RuleFor(x => x.Email).NotEmpty().WithErrorCode("email.required");
        RuleFor(x => x.Password).MinimumLength(8).WithErrorCode("password.too_short");
    }
}

public Task<Result<User>> RegisterAsync(RegisterUser command, CancellationToken ct) =>
    validator.ValidateToResultAsync(command, ct)          // the command, or every failure at once
        .BindAsync(valid => CreateUserAsync(valid, ct));

Result result = validator.Validate(command).ToResult();   // from a ValidationResult you already have
```

| FluentValidation | ResultUnion |
|---|---|
| `ErrorCode` (set stable ones with `WithErrorCode`) | `Error.Code` |
| `ErrorMessage` | `Error.Message` |
| `PropertyName` in camel case (`Lines[1].Quantity` → `lines[1].quantity`) | `Error.Field`; other naming with `ToResult(fieldName)` |
| number, string and Boolean placeholders (`MinLength`, `From`, `To`, …) | `Error.Arguments` (`minLength`, …) |
| one failure / several | the failure itself / a `validation.failed` group, as `CombineAll` does |

Every failure is a `Validation` error, whatever its `Severity`, as `IsValid` counts it. The attempted value is never
copied into the error, since it may be a password. Asynchronous rules (`MustAsync`) need `ValidateToResultAsync`, and
cancellation is not turned into a failure.

## HTTP clients: `ResultUnion.Http`

The client side of `ResultUnion.AspNetCore`: a response becomes a `Result`, and a problem details body becomes the
`Error` the server returned, so a service calling another service (or a Blazor or MAUI app calling its API) keeps working
with codes, types and field errors instead of status codes and JSON:

```csharp
using ResultUnion.Http;

Result<UserDto> user = await http.GetAsync($"/users/{email}", ct).ReadResultAsync<UserDto>(ct);
Result deleted = await http.DeleteAsync($"/users/{email}", ct).ReadResultAsync(ct);

if (user.TryGetError(out var error) && error.IsTransient) { /* retry later */ }
```

| The response | The result |
|---|---|
| 2xx | the JSON body as `T` (or a success without a value) |
| a problem from a ResultUnion server | the same `Error`: `code`, `category` (the `ErrorType`), message, `arguments`, one field error per `details` entry |
| a problem from another server | type from the status (404 NotFound, 409 Conflict, 429 TooManyRequests, 503 Unavailable, …), code `http.<status>`, message from `detail` or `title`, ASP.NET Core's `errors` as field errors |
| no body, or an HTML error page | type and code from the status, the reason phrase as the message |

A `Retry-After` header becomes the `retryAfterSeconds` argument. `Unexpected` errors arrive without the server's
message and arguments, which the server never sends. Only the response is interpreted: a request that fails to arrive
(`HttpRequestException`, a timeout) and a success body that is not valid JSON stay exceptions, so translate the ones you
expect with `Result.TryAsync`. The methods on `Task<HttpResponseMessage>` dispose the response; the ones on
`HttpResponseMessage` leave it to you.

## JSON: errors in messages between services

`Error` serializes with System.Text.Json out of the box (it carries `[JsonConverter]`), so an error can travel in an
event or a queue message where HTTP problem details and gRPC statuses do not apply:

```csharp
public sealed record RegistrationRejected(string Email, Error Reason);

var json = JsonSerializer.Serialize(new RegistrationRejected(email, error));
var received = JsonSerializer.Deserialize<RegistrationRejected>(json)!;    // received.Reason == error
```

```json
{
  "code": "user.too_young",
  "message": "Users must be at least 18; bob@example.com is 15.",
  "type": "BusinessRule",
  "field": "age",
  "arguments": { "minimumAge": 18, "age": 15 }
}
```

- The property names are fixed: they are a contract between services, so a naming policy does not change them.
- `type` is the `ErrorType` name (a number is accepted when reading); `field`, `arguments` and `details` are left out
  when empty, and details nest. Unknown properties are skipped, so a newer sender can add some.
- Arguments come back as JSON values: whole numbers as `int` (or `long`), other numbers as `decimal`, strings,
  booleans, `null`, and `JsonElement` for objects and arrays. A `Guid`, a date or an enum argument comes back as its
  string, so keep arguments to numbers and strings when the error must compare equal after the round trip.
- Malformed input (no code, message or type, an unknown type, wrong value kinds) throws `JsonException`.
- An `Unexpected` error keeps its message: this format is for trusted services. Clients get problem details or a gRPC
  status, which hide it.

Results themselves are not serialized: writing or reading one throws `NotSupportedException` (see "Things to know").
Send the value, or the error.

## OpenTelemetry: which errors, how often, in which request

ResultUnion reports failures through the .NET diagnostics APIs (`Meter`, `Activity`), so it needs no OpenTelemetry
package; the application's OpenTelemetry setup picks them up:

```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics.AddMeter(ResultTelemetry.MeterName).AddAspNetCoreInstrumentation())
    .WithTracing(tracing => tracing.AddAspNetCoreInstrumentation());
```

**Metrics, per operation.** End a pipeline with `Record` (or `RecordAsync`): a failure increments the counter
`resultunion.errors` and the result comes back unchanged.

```csharp
public Task<Result<User>> RegisterAsync(RegisterUser command, CancellationToken ct) =>
    Result.CombineAll(/* … */)
        .Bind(/* … */)
        .TapAsync(user => users.AddAsync(user, ct))
        .RecordAsync("users.register");
```

| Attribute | Value |
|---|---|
| `error.type` | the error code, e.g. `user.too_young` |
| `resultunion.error.category` | the `ErrorType`, e.g. `BusinessRule` |
| `resultunion.operation` | the name given to `Record`, e.g. `users.register` |

**Traces, with no code.** `ToHttpResult` and `GetValueOrThrowRpc` tag the request's server span with
`resultunion.error.code`, `resultunion.error.category` and `resultunion.error.field`, so a trace shows which error a
request ended with. `Record` adds the same attributes, plus a `resultunion.error` event, to the current span. The span
status becomes Error only for `Unexpected` and `Unavailable`: like a 4xx, the other categories are expected outcomes,
not server failures.

- The message and the arguments are never recorded: they may contain personal data (an email, a value the user typed),
  and the code identifies the error. Codes come from catalogs, so the attributes stay low-cardinality; keep operation
  names the same way (`users.register`, never an id).
- When nothing listens (no OpenTelemetry, or the meter not added), recording costs a check and does nothing.
- Where an error leaves by another way, such as a message consumer, call `ResultTelemetry.TagActivity(Activity.Current, error)`.

### See it in the Aspire Dashboard

`ResultUnion.WebApi` sends its traces, metrics and logs over OTLP to `http://localhost:4317` (set in
`launchSettings.json`). Start the standalone [Aspire Dashboard](https://aspire.dev/dashboard/standalone/) there, then the
API:

```powershell
docker run --rm -it -p 18888:18888 -p 4317:18889 --name aspire-dashboard mcr.microsoft.com/dotnet/aspire-dashboard:latest
```

```powershell
dotnet run --project ResultUnion.WebApi
```

Open the login link the container prints (`http://localhost:18888/login?t=…`), then use the browser UI at
`http://localhost:5258` (register a 15-year-old, an invalid email, a duplicate):

- **Traces:** a `POST /users/` span with status 422 carries `resultunion.error.code = user.too_young`,
  `resultunion.error.category = BusinessRule`, and a `resultunion.error` event with `resultunion.operation = users.register`.
- **Metrics:** meter `ResultUnion`, instrument `resultunion.errors`, filterable by `error.type`, category and operation.
- **Structured logs:** the use cases' own log lines, such as `Registration rejected: user.too_young`.

The dashboard is a development tool. Without the container the API runs as before; the exporter just has nowhere to send.

All the demos at once, with no container, under [Aspire](https://aspire.dev/):

```powershell
dotnet run --project ResultUnion.AppHost --launch-profile http
```

`ResultUnion.AppHost` starts the Web API, the gRPC service and the gRPC client, and opens Aspire's dashboard (its login
link is in the output). The client runs every call of the gRPC demo once; each call is one trace across both
resources, from the client's span (with the error it read back from the status, through `RecordAsync`) to the
service's span, which carries `grpc.status_code` and the same `resultunion.error.code`. The service's
`resultunion.errors` counts its failures by code; restart `grpcclient` from the dashboard for another round. The
AppHost sets each demo's OTLP endpoint and service name; the demos' own OpenTelemetry setup is unchanged.

## Testing: `ResultUnion.Testing`

Assertions for tests of code that returns results, for any test framework. A success returns its value, a failure its
error, and a failed assertion throws `ResultAssertionException` with the whole result in the message:

```csharp
using ResultUnion.Testing;

var email = Email.Create("ann@example.com").ShouldSucceed();
Email.Create("").ShouldFail("email.required").ShouldHaveType(ErrorType.Validation);
await service.RegisterAsync(command, ct).ShouldFailAsync("user.email_taken");
error.ShouldHaveFieldErrors(("email", "email.required"), ("password", "password.too_short"));
```

More in the [testing article](docs/articles/testing.md).

## Starting a project: `dotnet new resultunion-api`

```powershell
dotnet new install ResultUnion.Templates
```

```powershell
dotnet new resultunion-api -n Shop
```

The template creates a solution with a Minimal API and a test project (`--no-tests` leaves the tests out;
`--database sqlite` stores the products in SQLite through EF Core instead of in memory). Its one feature, products,
shows each convention in place: value objects with `Errors` catalogs (`ProductName`, `Price`), a use case as one
pipeline (`CombineAll` for every invalid field, `EnsureAsync` with the store's answer for a name that is taken,
`RecordAsync` for the failure metric), endpoints with `ToHttpResultAsync` and `ProducesErrors`, and tests with
`ShouldSucceedAsync` / `ShouldFailAsync("code")` and `WebApplicationFactory`. The projects reference the ResultUnion
packages of the template's own version. CI creates the variants from every build of the template and builds them with
warnings as errors and runs their tests.

In an existing project, `dotnet new resultunion-valueobject -n CustomerEmail` adds a string value object in the same
shape as `ProductName`: its codes (`customer_email.required`, `customer_email.too_long`) and messages come from the
name, the namespace from the project (`--namespace` sets another), and `--max-length` sets the limit (100 by default);
`--value-type int` or `decimal` makes a number with a range (`--min`, `--max`). `dotnet new resultunion-feature -n
OrderLine` adds a whole feature in that shape, in `OrderLines/`, with endpoints at `/order-lines` that `AddOrderLines()`
and `MapOrderLines()` register (`--database sqlite` for EF Core), and `dotnet new resultunion-feature-tests -n
OrderLine`, in the test project, its tests.

`dotnet new resultunion-grpc -n Catalog` creates the same feature behind gRPC: a `.proto` contract, a service whose
methods end with `GetValueOrThrowRpcAsync` (a failure becomes a status with `ErrorInfo` and `BadRequest`), and tests
that call it through the generated client and read failures back with `ToError()`.

A walk-through of the generated code, the responses it sends and how to add a feature of your own:
[Starting a project from the template](docs/articles/templates.md).

## Using ResultUnion in a DDD application

### Result or exception?

| Situation | Use | Why |
|---|---|---|
| Invalid input: a malformed email, a negative quantity | `Result` | Expected; the caller should show it to the user |
| A business rule says no: too young, already shipped, insufficient funds | `Result` | Part of the domain; it belongs in the signature |
| Not found, conflict, concurrency | `Result` | Expected outcomes the API maps to 404 / 409 |
| A programming error: a `null` argument, a state that cannot happen | exception | A bug; fail fast, do not hide it in a result |
| Infrastructure failure: the database or a service is down | exception, or `Result` at the boundary | Translate it only where you can say something useful (`Error.Unavailable`, `Error.Concurrency`) |
| Cancellation | exception | Not a failure of the operation; `Try` / `TryAsync` never catch it |

A rule of thumb: if the user can fix it or should be told about it, return a `Result`; if a developer has to fix it,
throw.

### Per layer

- **Domain.** Value object factories and entity methods return `Result` / `Result<T>`. Each concept has its own error
  catalog. Guard clauses for programming errors (`ArgumentNullException.ThrowIfNull`) stay exceptions. No HTTP, no
  ASP.NET Core: the domain references only `ResultUnion`.
- **Application.** Command handlers compose the domain: `CombineAll` for the value objects of a command,
  `Bind` / `Ensure` for the rules, `MapError` to translate infrastructure errors into domain ones. Cross-cutting
  behaviors (validation, authorisation, transactions) use `IResultFactory<TSelf>`.
- **Infrastructure.** Repositories return `T?` (turned into results with `FromNullable` / `ToResultAsync`) or `Result`.
  Exceptions that mean something to the domain are caught and translated here.
- **API.** `ToHttpResult` / `ToHttpResultAsync` from `ResultUnion.AspNetCore`, nothing else.

### Error catalog convention

```csharp
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

    public static Result<Email> Create(string? input) => /* … */;
}
```

- **One nested `Errors` class per concept**, next to the code that raises the errors: `Email.Errors.Required`.
- **Codes are `<concept>.<case>`** in snake_case, e.g. `user.too_young`, `order_line.quantity_not_positive`. They are
  a contract with clients: never change a published code, add a new one instead. Messages may change freely.
- **`static readonly` for fixed errors, a method for errors that carry values**, with those values as arguments
  (`WithArgument`), so clients can translate by code.
- **Whoever knows the field name sets it.** A value object that is itself one field (`Email`) does not set `Field`;
  the caller does, with `WithField("email")`. A value object with several fields (`OrderLine`) names its own
  (`product`, `quantity`), and collections add the index (`lines[1].quantity`).
- **Generic factories for generic errors.** Infrastructure-level errors (`Error.NotFound` from a repository) stay
  generic and are translated by the application layer, e.g. into `User.Errors.NotFound(email)`.

### A command, end to end

```csharp
// Domain
public sealed class User
{
    public static Result<User> Create(Email email, Password password) => /* invariants, returns User or an error */;
}

// Infrastructure: translate only what means something to the domain; any other exception propagates.
public Task<Result> SaveChangesAsync(CancellationToken ct) =>
    Result.TryAsync(
        async () => { await db.SaveChangesAsync(ct); },                      // a block lambda: no value
        (DbUpdateConcurrencyException _) => Error.Concurrency("The data was changed by someone else; reload and retry."));

// Application
public Task<Result<Guid>> Handle(RegisterUser command, CancellationToken ct) =>
    Result.CombineAll(
            Email.Create(command.Email).MapError(e => e.WithField("email")),
            Password.Create(command.Password).MapError(e => e.WithField("password")))
        .Bind(User.Create)                                                   // Result<User>
        .EnsureAsync(user => users.EnsureEmailIsFreeAsync(user.Email, ct))   // Task<Result<User>>
        .TapAsync(user => users.AddAsync(user, ct))
        .EnsureAsync(_ => unitOfWork.SaveChangesAsync(ct))                   // keeps the user on success
        .MapAsync(user => user.Id);

// API
app.MapPost("/users", (RegisterUser command, RegisterUserHandler handler, CancellationToken ct) =>
    handler.Handle(command, ct).ToHttpResultAsync(id => TypedResults.Created($"/users/{id}", new { id })));
```

`Result.TryAsync` with an untyped mapper (`ex => …`) catches every exception except cancellation, so it fits code
where every failure means the same thing (a remote call, a parser). With a typed mapper (`(DbUpdateConcurrencyException ex) => …`),
as above, only that exception type becomes an error and everything else propagates.

## Analyzers

The `ResultUnion` package includes Roslyn analyzers, so mistakes show up as compiler warnings (RU0001–RU0003,
RU0005, RU0009 and RU0010 with a code fix in the IDE), and the library's conventions as suggestions:

| Rule | Finds |
|---|---|
| `RU0001` | a `Result` that is discarded, so a failure would go unnoticed (use it, or discard it with `_ = ...`) |
| `RU0002` | an `async` lambda or a task-returning method passed to `Result.Try` (use `Result.TryAsync`) |
| `RU0003` | `Map` / `MapAsync` with a mapper that returns a `Result` (use `Bind` / `BindAsync`) |
| `RU0004` | `ResultValue` / `Error` read without checking the result (use `switch`, `Match` or `TryGetResultValue`) |
| `RU0005` | a `catch` of `OperationCanceledException` that returns a result (let cancellation propagate) |
| `RU0006` (Info) | an error created outside an error catalog (declare it in the concept's `Errors` class) |
| `RU0007` (Info) | `WithField` in a single-input value object or a catalog (set the field where the request is read) |
| `RU0008` (Info) | a method that returns a `ValueTask` of a result (return `Task<…>`) |
| `RU0009` | `Result<Error>`, usually `Map` with a mapper that returns an error (use `Bind`, `Ensure` or `MapError`) |
| `RU0010` | a `Result` written as or read from JSON, such as an endpoint that returns one (use `ToHttpResult`) |

Details, examples and how to change a rule's severity: [docs/ANALYZERS.md](docs/ANALYZERS.md).

## AI coding assistants: the ResultUnion skill

`.claude/skills/resultunion` is a [Claude skill](https://docs.claude.com/en/docs/claude-code/skills) that teaches an
assistant the library's conventions: error catalogs, who sets `Field`, `Bind` pipelines on `Task`, typed `TryAsync`,
adapters over external APIs, endpoints with `ProducesErrors`. To use it in your own project:

1. Copy `.claude/skills/resultunion/SKILL.md` and `.claude/skills/resultunion/references/` into your repository's
   `.claude/skills/resultunion/` (the `evals/` folder is only for testing the skill).
2. Add a line to your repository's `CLAUDE.md`:

   ```markdown
   Before writing or reviewing C# that uses ResultUnion, read `.claude/skills/resultunion/SKILL.md`.
   ```

The second step matters. In tests, assistants with the skill followed every convention checked (6 of 6 runs), while
without it 5 of 6 runs broke one or two, but an assistant rarely loads a skill on its own for code it thinks it can
write: with only the skill's description, it loaded in at most 17% of the requests that needed it. `CLAUDE.md` is read
in every session, so the pointer there makes it reliable.

## Performance

Measured with BenchmarkDotNet on .NET 11 ([docs/BENCHMARKS.md](docs/BENCHMARKS.md)):

| | Mean | Allocated |
|---|---:|---:|
| `switch` / `Match` / `TryGetResultValue` | 1–3 ns | 0 B |
| Four-step pipeline (`Ensure → Bind → Ensure → Map`) | 10–13 ns | 0 B |
| `CombineAll` of three successes | 14 ns | 0 B |
| Returning a cached catalog error | 4 ns | 0 B |
| `throw` + `catch`, for comparison | 1,239 ns | 376 B |
| Async handler chain, five steps, synchronous I/O | 66 ns | 256 B |

The success path allocates nothing; async chains allocate a task per step that creates a value, and steps that keep
the result (`EnsureAsync`, `TapAsync`, `TapErrorAsync`) reuse their source task.

## Things to know

- **Do not pass `async` lambdas to `Result.Try`.** They compile, but exceptions after the first `await` are not
  observed. Use `Result.TryAsync`.
- **Results are not serialized.** Writing or reading a `Result` or `Result<T>` with System.Text.Json throws
  `NotSupportedException`, with a message that says what to do; RU0010 reports it while you build, including an
  endpoint that returns a result. Without that, .NET 11 would write a `Result<T>`'s union case, and an endpoint would
  send a failure as `200 OK` with the error as the body. Map results to a response at the API boundary
  (`ToHttpResult`); in a message, send the value or the `Error`, which has its own JSON format.
- **`Result<Error>` is not supported.** Both cases would have the type `Error`, so conversions are ambiguous (CS0457).
- **Union members are hidden from IntelliSense.** `Value`, `HasValue` and the two `TryGetValue` overloads exist for
  the compiler's pattern matching. Use `TryGetResultValue` and `TryGetError`; `TryGetValue(out var x)` does not compile
  because the two overloads make `out var` ambiguous.
- **Other languages.** The `T` / `Error` → `Result<T>` conversion is a C# 15 union conversion. VB, F# and projects with
  `LangVersion` below 15 use `Result<T>.Success` and `Result<T>.Failure`.
- **Equality.** Results and errors compare by value: two successes by their values, two failures by their errors. A
  `default(Result<int>)` equals `Result<int>.Failure(Error.DefaultResult)`, because both carry that error.

## Documentation site

New to the library? Start with [Getting started](docs/articles/getting-started.md), then the articles on
[EF Core](docs/articles/ef-core.md), [pipeline behaviors](docs/articles/pipeline-behaviors.md),
[testing](docs/articles/testing.md) and [translating error messages](docs/articles/localization.md). Coming from
another Result library: [Moving from FluentResults or ErrorOr](docs/articles/migration.md).

This guide, the articles, the other pages in `docs/` and an API reference for the packages are also built as a website with DocFX
(`docfx.json`, the landing page and navigation in `docs/site/`). How to build it locally and publish it on GitHub Pages:
[docs/RELEASING.md](docs/RELEASING.md#documentation-site).

## Releases

Changes per version are in [CHANGELOG.md](CHANGELOG.md); how a version is published is in
[docs/RELEASING.md](docs/RELEASING.md). What is left before 1.0, and what 1.x promises (the C# API, documented
behavior, the wire formats and the analyzer rules): [docs/ROADMAP.md](docs/ROADMAP.md).

## Contributing

Questions and feedback go to [Discussions](https://github.com/kaldanig/ResultLabIUnion/discussions); for the 1.0
release candidates, the feedback threads say what helps most: the
[API](https://github.com/kaldanig/ResultLabIUnion/discussions/2) (rc.1) and the
[templates](https://github.com/kaldanig/ResultLabIUnion/discussions/3) (rc.2). Issues and pull requests are welcome. How to build, test and what a pull request needs:
[CONTRIBUTING.md](https://github.com/kaldanig/ResultLabIUnion/blob/master/CONTRIBUTING.md). Everyone who takes part
follows the [code of conduct](https://github.com/kaldanig/ResultLabIUnion/blob/master/CODE_OF_CONDUCT.md).

## Security

Report a vulnerability privately through GitHub (**Security → Report a vulnerability**); what is in scope and how it is
handled is in [SECURITY.md](https://github.com/kaldanig/ResultLabIUnion/blob/master/SECURITY.md). The code is scanned by
CodeQL on every push and pull request, and Dependabot reports vulnerable dependencies.

## License

MIT, see [LICENSE](https://github.com/kaldanig/ResultLabIUnion/blob/master/LICENSE).
