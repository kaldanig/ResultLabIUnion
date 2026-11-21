# Design decisions

Why ResultUnion is built the way it is. The [README](../README.md) shows *how* to use the library; this file records
*why*, with the alternatives that were considered and what each choice costs.

Each entry follows the same shape: **Context** (the problem), **Decision** (what we chose), **Alternatives** (what we
rejected and why), **Consequences** (what to keep in mind). Where a claim depends on compiler or runtime behaviour, it
was checked with a small program, and the observed output is quoted.

## Contents

- **Types:** [1 Unions](#1-result-is-a-c-15-union) · [2 Structs](#2-results-are-readonly-record-structs) ·
  [3 Default is a failure](#3-a-default-result-is-a-failure) · [4 Value decided by IsSuccess](#4-the-union-case-is-decided-by-issuccess)
- **API:** [5 Bind](#5-bind-not-then) · [6 No implicit operators on Result&lt;T&gt;](#6-no-user-defined-conversions-on-resultt) ·
  [7 TryGetResultValue](#7-trygetresultvalue-and-hidden-union-members) · [8 IResultState](#8-iresultstate-not-iresult) ·
  [9 IResultFactory](#9-iresultfactorytself-has-failure-but-no-success)
- **Async:** [10 Task](#10-task-not-valuetask) · [11 Eager validation](#11-arguments-are-validated-eagerly) ·
  [12 Cancellation tokens](#12-cancellation-tokens-are-passed-by-closure) · [13 Sequential collections](#13-combineallasync-is-sequential)
- **Exceptions:** [14 Cancellation is never caught](#14-cancellation-is-never-caught) · [15 Required mapper](#15-the-exception-mapper-is-required) ·
  [16 Typed Try](#16-one-exception-type-through-a-typed-mapper) · [17 No async guard](#17-no-compile-time-guard-against-async-lambdas-in-try)
- **Errors:** [18 Field, Details, Arguments](#18-field-details-and-arguments-on-one-error-type) · [19 Equality](#19-hand-written-value-equality) ·
  [20 CombineAll](#20-combineall-semantics) · [21 Flatten](#21-nested-groups-are-flattened-for-reporting)
- **ErrorType:** [22 Categories describe actions](#22-a-category-describes-what-the-caller-should-do) ·
  [23 Closed enum](#23-errortype-is-a-closed-enum) · [24 Concurrency is transient](#24-concurrency-is-transient)
- **Boundaries:** [25 Separate ASP.NET Core project](#25-aspnet-core-integration-is-a-separate-project) ·
  [26 Unexpected stays private](#26-unexpected-errors-never-reach-the-client) · [27 No JSON for results](#27-results-are-not-serialized-directly) ·
  [30 Typed HTTP responses](#30-http-responses-are-typed) · [31 gRPC rich errors](#31-grpc-errors-use-the-rich-error-model)
- **DDD:** [28 Error catalogs](#28-error-catalogs-per-domain-concept) · [29 Who sets Field](#29-whoever-knows-the-field-name-sets-it)

---

## Types

### 1. Result is a C# 15 union

**Context.** A result is either a value or an error. Callers should be able to handle both cases with the language's
own tools, and the compiler should complain when a case is forgotten.

**Decision.** `Result<T>` is a `[Union]` type implementing `IUnion`, with the cases `T` and `Error`. It supports
`switch` and `is` directly, and the non-boxing access pattern (`HasValue`, `TryGetValue`), so matching a value-type
`T` does not allocate.

```csharp
string Describe(Result<User> result) => result switch
{
    User user   => $"user {user.Email}",
    Error error => $"error {error.Code}",
};
```

**Alternatives.** A class hierarchy (`Success<T>` / `Failure<T>`) allocates every result and needs a type test per
case. A hand-written `Match` only, without language support, is more verbose and gives no exhaustiveness checking.

**Consequences.** The library requires C# 15 (`net11.0`). The union contract adds public members that only the compiler
should use ([7](#7-trygetresultvalue-and-hidden-union-members)). `Result` (no value) is not a union: it has a single
interesting case.

### 2. Results are readonly record structs

**Context.** Results are created on every call of every operation that can fail.

**Decision.** `Result<T>` and `Result` are `readonly record struct`s: no heap allocation on the success path, value
equality for free, and no defensive copies.

**Alternatives.** Classes would allocate for every result and could be `null`, which a result type should rule out.

**Consequences.** Results stored as `IResultState` (for example in `Combine(IEnumerable<IResultState>)`) are boxed; the
XML docs say so. Structs have a `default` value, which [3](#3-a-default-result-is-a-failure) deals with.

### 3. A default result is a failure

**Context.** `default(Result<T>)` exists, through arrays, uninitialised fields or `default` literals, and must mean
something.

**Decision.** A default result is a failure carrying `Error.DefaultResult` (`result.default`). `Error` is never `null`
on a failure.

**Alternatives.** Treating `default` as "no case" would make every `switch` need a `null` arm and every caller check a
third state.

**Consequences.** One quirk remains: `default(Result<int>) == Result<int>.Failure(Error.DefaultResult)` is `false`,
because record equality compares the backing field (`null`) rather than the `Error` property. It is documented and
deliberately not locked in by a test.

### 4. The union case is decided by IsSuccess

**Context.** The first version computed `Value` as "`_value` if it is not `null`, otherwise the error". For a value-type
`T`, `_value` is never `null`, so every failed `Result<int>` matched as `int 0`:

```text
bad switch { int i => $"ok {i}", Error e => $"err {e}" }   // printed "ok 0" for a failure
```

**Decision.** `Value => IsSuccess ? _value : Error`. All internal code branches on `IsSuccess`, never on the value.

**Consequences.** Regression tests cover failures and `default` for value types, reference types and pattern
matching. Internally, methods read `_value!` only inside an `IsSuccess` branch, so the state is checked once rather than
again through `ResultValue`.

## API

### 5. Bind, not Then

**Context.** The chaining operation needed one name across `Result` and `Result<T>`.

**Decision.** `Bind`. Together with `Map` it is the standard pair: `Map` takes `T → U`, `Bind` takes `T → Result<U>`,
and the names make the difference visible.

**Alternatives.** `Then` reads well in English (and is used by ErrorOr), but next to `Map` it hides which one can fail.

**Consequences.** Readers who know CSharpFunctionalExtensions, LanguageExt or FluentResults recognise the name.

### 6. No user-defined conversions on Result&lt;T&gt;

**Context.** `return value;` and `return error;` should work in methods returning `Result<T>`.

**Decision.** `Result<T>` has no `implicit operator`s: C# 15 union conversions already convert each case to the union.
This was checked for generic `T` and for interface-typed `T` without operators. `Result` keeps its `Error → Result`
operator because it is not a union.

**Alternatives.** Keeping the operators would help VB, F# and projects with `LangVersion` below 15. They were removed
to keep one conversion path; with an interface `T`, user-defined conversions are not even considered.

**Consequences.** Other languages and older language versions use `Result<T>.Success` / `Failure`; the type's docs say so.

### 7. TryGetResultValue and hidden union members

**Context.** The union contract requires `Value`, `HasValue`, `TryGetValue(out T)` and `TryGetValue(out Error?)`. With
two `TryGetValue` overloads, `out var` does not compile:

```text
result.TryGetValue(out var user)   // error CS0121: the call is ambiguous
```

**Decision.** The four union members stay public (the compiler needs them) but are hidden from IntelliSense with
`[EditorBrowsable(Never)]`. `TryGetResultValue(out T)` and `TryGetError(out Error?)` are the visible, unambiguous pair.

**Consequences.** `Value` boxes value types; users never need it. Visual Studio ignores `EditorBrowsable` for project
references in the same solution, so the hiding only shows for package consumers.

### 8. IResultState, not IResult

**Context.** The common interface was first called `IResult`. ASP.NET Core's Web SDK imports
`Microsoft.AspNetCore.Http`, whose `IResult` is the core Minimal API type, so any web project using both would hit
CS0104 (ambiguous reference).

**Decision.** `IResultState` / `IResultState<T>`.

**Consequences.** Renamed before anything was published, so no compatibility cost.

### 9. IResultFactory&lt;TSelf&gt; has Failure but no Success

**Context.** CQRS pipeline behaviors (validation, authorisation, transactions) wrap handlers returning `Result`,
`Result<OrderId>`, `Result<UserDto>`… and must short-circuit with a failure of a type they only know as `TResponse`.

**Decision.** `IResultFactory<out TSelf>` with `static abstract TSelf Failure(Error)`, implemented by both result types.
It extends `IResultState`, so a behavior can also inspect `IsSuccess` / `Error` (for example to commit or roll back).

**Alternatives.** Reflection or per-type behavior registrations. A generic `Success` was left out on purpose: a
successful `Result<T>` needs a value the behavior does not have.

## Async

### 10. Task, not ValueTask

**Context.** Async variants could take `Task`, `ValueTask`, or both.

**Decision.** `Task` only.

**Alternatives.** Offering both makes ordinary `async` lambdas ambiguous:

```text
Run(async x => { await Task.Yield(); return x + 1; });
// error CS0121: The call is ambiguous between 'Run(Func<int, Task<int>>)' and 'Run(Func<int, ValueTask<int>>)'
```

`ValueTask` alone would force callers to wrap the `Task`s returned by EF Core, `HttpClient` and ASP.NET Core, and adds
its usage rules (await once, never concurrently). Its benefit, avoiding an allocation on synchronous completion, is
negligible next to the I/O in a typical pipeline.

**Consequences.** A `ValueTask` source needs `.AsTask()`. Extensions on `ValueTask<Result<T>>` *sources* (not delegates)
could be added later without the ambiguity.

### 11. Arguments are validated eagerly

**Context.** In an `async` method, `ArgumentNullException.ThrowIfNull` does not throw at the call: the exception is
stored in the returned task and appears only when it is awaited.

**Decision.** Every async method validates its arguments in a non-async body and then calls a local `async` function.
A `null` source or delegate throws immediately; exceptions thrown by the delegates themselves surface through the task.

**Consequences.** Tests use an `Action`-based helper (`ThrowsEagerly`) on purpose: `Assert.ThrowsAsync` would also pass
for the lazy behaviour this rules out.

### 12. Cancellation tokens are passed by closure

**Decision.** Async delegates do not take a `CancellationToken`; callers capture it: `user => orders.CreateAsync(user, ct)`.

**Alternatives.** A token parameter on every method would double the number of overloads.

### 13. CombineAllAsync is sequential

**Decision.** `CombineAllAsync` awaits one element at a time.

**Alternatives.** Running elements concurrently is faster, but the typical selector is a repository call on an EF Core
`DbContext`, which does not allow parallel operations. A caller that wants concurrency can start the tasks and combine
the finished results.

## Exceptions

### 14. Cancellation is never caught

**Decision.** `Try` / `TryAsync` never catch `OperationCanceledException` (or `TaskCanceledException`), not even when the
typed overload's exception type is a base type of it.

**Why.** Cancellation is not a failure of the operation: the caller asked to stop. Turning it into an `Error` would
make it look like a business outcome and could be mapped to an HTTP error.

### 15. The exception mapper is required

**Decision.** `Try` takes a `Func<Exception, Error>`; there is no overload that maps to `Error.Unexpected(ex.Message)`
by default.

**Why.** A default would silently swallow every exception and copy internal messages into errors that may reach clients.
The exception itself is not stored on `Error` (it would complicate serialization); log it inside the mapper.

### 16. One exception type through a typed mapper

**Context.** Often only one exception means something to the caller (a `DbUpdateConcurrencyException`, a
`FormatException`), and the rest are bugs or outages that should surface.

**Decision.** Overloads `Try<T, TException>` / `TryAsync<T, TException>` catch only `TException`. The type is inferred
from the mapper's typed parameter, so no explicit type arguments are needed:

```csharp
Result.Try(() => int.Parse(text), (FormatException ex) => Error.Validation(ex.Message));   // only FormatException
Result.Try(() => int.Parse(text), ex => Error.Validation(ex.Message));                     // every exception
```

**Consequences.** An untyped mapper, or one typed as `Exception`, keeps the catch-all overload (the non-generic one is
the better match); this is covered by tests. A `Task<T>`-returning delegate picks the overload with a value; a block
lambda (`async () => { await …; }`) picks the one without.

### 17. No compile-time guard against async lambdas in Try

**Context.** `Result.Try(async () => …)` compiles: `T` becomes a `Task`, and exceptions after the first `await` are not
observed. For a while, `[Obsolete(error: true)]` overloads taking `Func<Task>` turned this into a compile error.

**Decision.** The guard overloads were removed; the docs warn instead.

**Consequences.** The mistake is no longer caught by the compiler. The guards also made every lambda that only throws
(`() => throw new X()`) ambiguous, which was confusing in tests and in real code.

## Errors

### 18. Field, Details and Arguments on one error type

**Context.** APIs need every invalid field at once, with its name, and clients want to translate messages.

**Decision.** `Error` carries an optional `Field`, a list of `Details` (a group of errors), and named `Arguments` (the
values behind the message). All three are optional, read-only and never `null`.

**Alternatives.** A `ValidationError : Error` subtype would need an unsealed hierarchy and type tests. An untyped
metadata dictionary would lose the structure the HTTP mapping relies on.

**Consequences.** `WithField`, `WithFieldPrefix` and `WithArgument` return copies; they preserve the other two parts.
The library does no localization: it hands the client a `code` and `arguments`.

### 19. Hand-written value equality

**Context.** A record compares collection properties by reference, so two errors with equal details would differ.

**Decision.** `Error.Equals` compares `Code`, `Message`, `Type`, `Field`, `Details` element by element in order, and
`Arguments` by name and value in any order. `GetHashCode` uses the scalar parts and the counts.

### 20. CombineAll semantics

**Context.** `Combine` stops at the first failure, which suits sequential checks but not forms.

**Decision.** `CombineAll` (for `Result`, for two to four `Result<T>` with tuple values, and for sequences) reports every
failure. A **single failure is returned unchanged**; several are grouped into `Error.Validation(details)`, whose type is
always `Validation`, whatever the types of its details.

**Alternatives.** Always wrapping would make the common one-error case nested; taking the group's type from its details
would need rules for mixed types.

**Consequences.** Readers check `Details.Count > 0`, or use `Flatten()`, which handles both shapes. `CombineAll` is meant
for independent inputs; a `NotFound` among them becomes part of a 400 response.

### 21. Nested groups are flattened for reporting

**Context.** A collection whose elements each report several fields produces groups inside groups.

**Decision.** `Error.Flatten()` returns the innermost errors, each with its full path (`lines[1].product`), and
`ToProblemDetails` uses it, so a client sees one entry per invalid input.

## ErrorType

### 22. A category describes what the caller should do

**Decision.** An `ErrorType` tells the caller how to react: fix the input, sign in, wait and retry, reload. The specific
reason is the `Code`. A new category is added only when callers must react differently.

**Consequences.** `TooManyRequests` (429) was added because clients and proxies handle it specially. `Timeout`, `Gone`,
`PreconditionFailed`, `PayloadTooLarge`, `PaymentRequired`, `Locked` and `NotImplemented` were rejected: they are
covered by an existing category plus a specific code, or they are HTTP transport concerns that ASP.NET Core already
handles.

### 23. ErrorType is a closed enum

**Decision.** A plain enum, extended only in the library.

**Alternatives.** An open "smart enum" class would let applications add categories, but every mapping (`ToStatusCode`,
`IsTransient`) would need a default for unknown kinds and `switch` would lose its simplicity.

**Consequences.** Unknown values map to 500 and are not transient. A test fails when a member is added, as a reminder to
decide its retry semantics.

### 24. Concurrency is transient

**Decision.** `IsTransient` is `true` for `Unavailable`, `TooManyRequests` and `Concurrency`.

**Why.** Running the *whole operation* again, so it reads fresh data, can succeed after a concurrency conflict.
Resending the same stale update cannot; the docs make that distinction.

## Boundaries

### 25. ASP.NET Core integration is a separate project

**Decision.** `ResultUnion.AspNetCore` holds the status-code mapping, problem details and `ToHttpResult`. The core
library, and therefore the domain, has no ASP.NET Core dependency. The tests are split the same way.

### 26. Unexpected errors never reach the client

**Decision.** For `ErrorType.Unexpected`, `ToProblemDetails` sends a fixed `detail` ("An unexpected error occurred.")
and no `arguments`.

**Why.** Unexpected errors usually come from exceptions, whose messages can expose internals. Log them on the server.

### 27. Results are not serialized directly

**Context.** Observed with System.Text.Json: a `Result<int>` success serializes as just `1`, a failure as the error
object, and a successful `Result` throws `InvalidOperationException` because its `Error` getter throws.

**Decision.** No JSON converter. Results are mapped at the API boundary, to a DTO or to problem details.

**Consequences.** The README warns against returning results from controllers or storing them as JSON.

### 30. HTTP responses are typed

**Context.** The first `ToHttpResult` returned `IResult`. Observed: Minimal API endpoints returning `IResult` get no
response metadata, so OpenAPI documents nothing, while `Results<Ok<Dto>, ProblemHttpResult>` yields `200 Dto`
(also through `Task<…>`), `Results<Created<Dto>, …>` yields `201 Dto` and `Results<NoContent, …>` yields `204`.
`ProblemHttpResult` contributes no metadata, because its status is only known at run time.

**Decision.** `ToHttpResult` / `ToHttpResultAsync` return `Results<TSuccess, ProblemHttpResult>`, with `TSuccess`
inferred from the success lambda (`Ok<T>` and `NoContent` by default). An untyped overload taking `Func<…, IResult>`
remains for successes that can be one of several responses. `ProducesErrors(params ErrorType[])` declares the problem
responses with the same status mapping as the responses themselves (`HttpValidationProblemDetails` for 400).

**Alternatives.** Keeping `IResult` and asking for `.Produces<T>()` on every endpoint duplicates the success type and
drifts from the code. Typing the problems per `ErrorType` (e.g. `Results<Ok<T>, NotFound<ProblemDetails>, Conflict<…>>`)
would make every signature long and still not match the runtime choice.

**Consequences.** This changed the return types of existing methods: code that compared the result to a concrete type
now unwraps `.Result`. `Results<…>` still implements `IResult`, so endpoints compile unchanged. A lambda that returns two
different response types infers no `TSuccess` and falls back to the untyped overload.

### 31. gRPC errors use the rich error model

**Context.** gRPC methods return the reply message; failures travel in the call's status. The question was how to
carry an `Error` (type, code, arguments, fields) over gRPC, and how a client gets it back.

**Decision.** A separate `ResultUnion.Grpc` project (no ASP.NET Core; usable by servers and clients). At the service
boundary, `GetValueOrThrowRpc` returns the reply or throws an `RpcException` carrying a `google.rpc.Status`: the status
code from `ErrorType`, an `ErrorInfo`, a `BadRequest` for field violations, and a `RetryInfo` for rate limits.
`RpcException.ToError()` restores the `Error` on the client.

- `ErrorInfo.reason` is the code in UPPER_SNAKE_CASE, as gRPC requires; the original code, the `ErrorType` and the
  original code of each field violation go into `ErrorInfo.metadata`, so the round trip is lossless where it can be.
- Conflict and BusinessRule both map to FailedPrecondition, because a conflict is not always a duplicate
  (AlreadyExists); the `type` in metadata tells them apart again on the client.
- Concurrency maps to Aborted, which gRPC defines as "retry the whole operation", the same meaning as `IsTransient`.

**Alternatives.** Putting errors into the reply (`oneof { Reply reply; Error error; }`) is typed, but bypasses what gRPC
tooling relies on: status codes for retries, deadlines, interceptors and monitoring. It remains the right choice for
per-item errors in a stream, where a status would end the stream.

**Consequences.** `ErrorInfo.metadata` is a string map, so argument values arrive as strings. `FieldViolation` has no
metadata, so the arguments of individual field violations are not transmitted. A status from a non-ResultUnion server
still becomes an `Error`, with the code `grpc.<status>`.

## DDD

### 28. Error catalogs per domain concept

**Decision.** Each concept declares its errors in a nested `Errors` class (`Email.Errors.Required`), with codes in the
form `<concept>.<case>`. Fixed errors are `static readonly`; errors with values are methods that add arguments.

**Why.** Codes are a contract with clients (they switch on them and translate them), so they need one obvious place and
must not change once published. Messages may change freely.

### 29. Whoever knows the field name sets it

**Decision.** A value object that is itself one field (`Email`) does not set `Field`; the caller does
(`WithField("email")`). A value object with several fields (`OrderLine`) names its own, and collections add the index
(`lines[2].quantity`). Infrastructure errors stay generic (`Error.NotFound` from a repository) and the application layer
translates them into domain errors with `MapError`.
