# Changelog

All notable changes to the ResultUnion packages are listed here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow [Semantic Versioning](https://semver.org/).
Before 1.0, a minor version may contain breaking changes; they are listed under **Changed** or **Removed**.

The public API of each package is recorded in its `PublicAPI.Shipped.txt`, so the exact changes between two versions
are in the diff of those files.

## [Unreleased]

### ResultUnion

#### Changed

- **Breaking:** writing or reading a `Result` or `Result<T>` with System.Text.Json throws `NotSupportedException`,
  with a message that says what to do instead. Before, .NET 11 wrote a `Result<T>`'s union case, so a Minimal API
  endpoint that returned a result sent a failure as `200 OK` with the error as the body, and a successful `Result`
  threw an unhelpful `InvalidOperationException`. `Error` still serializes as before.

### ResultUnion (analyzers)

- RU0010 (Warning): a result written as or read from JSON: a Minimal API handler that returns one, or one passed to
  `JsonSerializer`, `Results` / `TypedResults`, `WriteAsJsonAsync` or the `HttpClient` JSON methods.

## [0.3.0] - 2026-11-22

### Upgrading from 0.2.0

- **`IResultState` is now `IAnyResult`.** Replace the name; the members are the same. `IResultState<T>` is gone: use
  `Result<T>` itself, or `IAnyResult` where results of different value types are mixed
  (`Result.Combine(IEnumerable<IAnyResult>)`). Code that only uses `Result`, `Result<T>`, `Error` and
  `IResultFactory<TSelf>` compiles unchanged, but a library compiled against 0.2.0 that used `IResultState` must be
  recompiled.
- **Two new warnings may appear when you build.** RU0005 (a cancellation caught and returned as a result) and RU0009
  (a `Result<Error>`, usually `Map` where `Bind` or `Ensure` was meant) are warnings; a project with
  `TreatWarningsAsErrors` fails on them until they are fixed. Each one points at a real problem; the rules, with
  examples, are in [docs/ANALYZERS.md](docs/ANALYZERS.md). RU0006–RU0008 are suggestions that show only in the IDE.
- **Equality.** `default(Result<T>)` now equals `Result<T>.Failure(Error.DefaultResult)`, and the two have the same
  hash code. Nothing else about equality changed.
- **New, optional packages:** `ResultUnion.Http` (an HTTP response read back as a result), `ResultUnion.Testing` (test
  assertions) and `ResultUnion.Templates` (`dotnet new resultunion-api`).

### ResultUnion

#### Changed

- **Breaking:** `IResultState` is renamed `IAnyResult`, and `Combine` / `CombineAll` take `IEnumerable<IAnyResult>`.
  The members (`IsSuccess`, `IsFailure`, `Error`) are the same; replace the name.
- `Result` and `Result<T>` compare by what they expose: the case, then the value of a success or the error of a
  failure. A `default` result now equals `Failure(Error.DefaultResult)` (`==`, `Equals` and `GetHashCode`); before, the
  record equality compared the fields, and the two were not equal.

#### Removed

- **Breaking:** `IResultState<T>`. No API of the library took it; use `Result<T>`, or `IAnyResult` for results of
  different value types.

### ResultUnion (analyzers)

- RU0005 (Warning): a `catch` of `OperationCanceledException` / `TaskCanceledException` without a `when` filter that
  returns a result, turning cancellation into a failure.
- RU0006 (Info): an error created outside an error catalog (a type named `Errors` or ending in `Errors`).
- RU0007 (Info): `WithField` in a single-input value object's factory or in an error catalog, which cannot know the
  field's name.
- RU0008 (Info): a method, local function or property that returns a `ValueTask` of a result.
- RU0009 (Warning): a `Result<Error>`, where it is written and where a call such as `Map(x => error)` produces it.

### ResultUnion.Templates (new package)

- `dotnet new resultunion-api`: a Minimal API with one feature (products) that follows the conventions (value objects
  with error catalogs, a use case as one pipeline, endpoints with `ToHttpResultAsync` and `ProducesErrors`), and a test
  project with ResultUnion.Testing and `WebApplicationFactory`; `--no-tests` leaves the tests out. The projects
  reference the ResultUnion packages of the template's version.

### ResultUnion.Testing (new package)

- Test assertions that need no test framework: `ShouldSucceed()` returns the value, `ShouldFail()` /
  `ShouldFail(code)` return the error (with `…Async` forms on tasks), and `ShouldHaveCode`, `ShouldHaveType`,
  `ShouldHaveField`, `ShouldHaveArgument` (numbers compared by value) and `ShouldHaveFieldErrors` (every field error, in
  any order) check it. A failed assertion throws `ResultAssertionException` with the whole result in its message.

### ResultUnion.Http (new package)

- `HttpResponseMessage.ReadResultAsync<T>()` / `ReadResultAsync()` and the same on `Task<HttpResponseMessage>` (which
  dispose the response): the value from a successful response's JSON body, or the error read from its RFC 9457 problem
  details body. From a ResultUnion server the error comes back as it was sent (code, type, message, arguments, field
  errors); from any other server the type and code follow the status, ASP.NET Core's `errors` become field errors and
  `Retry-After` becomes `retryAfterSeconds`.
- `HttpResponseMessage.ReadErrorAsync()`: the error of a failed response.

### ResultUnion.AspNetCore

- Problem responses carry the error's type as the `category` member, so a client can tell a `Concurrency` conflict
  (transient) from a `Conflict`, which share status 409.

## [0.2.0] - 2026-11-22

### ResultUnion.FluentValidation (new package)

- `ValidationResult.ToResult()` / `ToResult(fieldName)`: a success, one failure as it is, or several grouped in
  `validation.failed`, each with the rule's error code, message, the property path in camel case as its field and the
  rule's number, string and Boolean placeholders as arguments. The attempted value is never copied.
- `IValidator<T>.ValidateToResult` / `ValidateToResultAsync`: the validated value as a `Result<T>`.

### ResultUnion.Grpc

#### Changed

- `GetValueOrThrowRpc` and `GetValueOrThrowRpcAsync` reject a `null`, empty or white-space `domain` when they are
  called. Before, a bad domain was noticed only when a failure was turned into an `RpcException`, and in the async
  forms only after the result had been awaited.

## [0.1.0] - 2026-11-21

The first release. Requires .NET 11 (C# 15 unions).

### ResultUnion

- `Result<T>` (a C# 15 union of the value and `Error`) and `Result`, as readonly record structs: switching on a result
  does not allocate.
- `Error` with a stable `Code`, an `ErrorType` (Validation, Unauthorized, Forbidden, NotFound, Conflict, Concurrency,
  BusinessRule, TooManyRequests, Unavailable, Unexpected), `Field`, nested `Details`, `Arguments` for translated
  messages, `Flatten` and `WithFieldPrefix`; `error.IsTransient`.
- Pipelines: `Map`, `Bind`, `Ensure`, `Tap`, `TapError`, `MapError`, `Match`, with `…Async` forms on results and on
  `Task<Result…>`. Async steps that keep the result reuse their source task.
- `Result.CombineAll` for two to four results (as a tuple), for spans, and for sequences with indexed field names
  (`lines[2].quantity`); `CombineAllAsync`.
- `Result.Try` / `Result.TryAsync`, catching every exception or one type through a typed mapper; cancellation is never
  caught. `Result.FromNullable` and `ToResultAsync` on `Task<T?>` and `ValueTask<T?>` (EF Core's `FindAsync`).
- The async pipeline also on `ValueTask<Result<T>>` and `ValueTask<Result>` sources: each source is awaited once and
  the chain continues on `Task`; delegates stay `Task`-based.
- `IResultState` and `IResultFactory<TSelf>` for generic pipeline behaviors.
- `ErrorJsonConverter`: `Error` serializes with System.Text.Json in a fixed format, for events and queue messages.
- `ResultTelemetry` and `Record` / `RecordAsync`: failures counted in OpenTelemetry (`resultunion.errors`) and added to
  the current span, through `System.Diagnostics`, without an OpenTelemetry dependency.
- Analyzers in the package: RU0001 (a discarded result), RU0002 (asynchronous code in `Result.Try`), RU0003 (`Map`
  with a mapper that returns a result), each with a code fix (`_ =`, `TryAsync`, `Bind`), and RU0004 (`ResultValue`
  or `Error` read without checking the result). They also apply in a project that installs only `ResultUnion.AspNetCore`
  or `ResultUnion.Grpc`.
- Built with .NET 11 Runtime Async.

### ResultUnion.AspNetCore

- `ToHttpResult` / `ToHttpResultAsync`: typed Minimal API results (`Results<Ok<T>, ProblemHttpResult>`, `Created`,
  `NoContent`, …), so OpenAPI documents the success response.
- RFC 9457 problem details with `code`, `arguments`, per-field `errors` and `details`; `Unexpected` errors hide their
  message.
- `ProducesErrors(...)` to declare the possible problem responses in OpenAPI.
- Problem responses tag the request's span with the error code and category.

### ResultUnion.Grpc

- `GetValueOrThrowRpc` / `GetValueOrThrowRpcAsync` for services: failures become an `RpcException` with the
  `google.rpc` rich error model (`ErrorInfo`, `BadRequest`, `RetryInfo`).
- `RpcException.ToError()` for clients, restoring type, code, message, arguments and field violations.
- Failures that leave a service tag the call's span with the error code and category.

[Unreleased]: https://github.com/kaldanig/ResultLabIUnion/compare/v0.3.0...HEAD
[0.3.0]: https://github.com/kaldanig/ResultLabIUnion/compare/v0.2.0...v0.3.0
[0.2.0]: https://github.com/kaldanig/ResultLabIUnion/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/kaldanig/ResultLabIUnion/releases/tag/v0.1.0
