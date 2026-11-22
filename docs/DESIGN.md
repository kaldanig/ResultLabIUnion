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
  [7 TryGetResultValue](#7-trygetresultvalue-and-hidden-union-members) · [8 IAnyResult](#8-ianyresult-not-iresult) ·
  [9 IResultFactory](#9-iresultfactorytself-has-failure-but-no-success)
- **Async:** [10 Task](#10-task-not-valuetask) · [11 Eager validation](#11-arguments-are-validated-eagerly) ·
  [12 Cancellation tokens](#12-cancellation-tokens-are-passed-by-closure) · [13 Sequential collections](#13-combineallasync-is-sequential) ·
  [32 Runtime Async](#32-built-with-net-11-runtime-async) · [33 No unneeded tasks](#33-async-steps-create-no-task-they-do-not-need)
- **Exceptions:** [14 Cancellation is never caught](#14-cancellation-is-never-caught) · [15 Required mapper](#15-the-exception-mapper-is-required) ·
  [16 Typed Try](#16-one-exception-type-through-a-typed-mapper) · [17 No async guard](#17-no-compile-time-guard-against-async-lambdas-in-try)
- **Errors:** [18 Field, Details, Arguments](#18-field-details-and-arguments-on-one-error-type) · [19 Equality](#19-hand-written-value-equality) ·
  [20 CombineAll](#20-combineall-semantics) · [21 Flatten](#21-nested-groups-are-flattened-for-reporting)
- **ErrorType:** [22 Categories describe actions](#22-a-category-describes-what-the-caller-should-do) ·
  [23 Closed enum](#23-errortype-is-a-closed-enum) · [24 Concurrency is transient](#24-concurrency-is-transient)
- **Boundaries:** [25 Separate ASP.NET Core project](#25-aspnet-core-integration-is-a-separate-project) ·
  [26 Unexpected stays private](#26-unexpected-errors-never-reach-the-client) · [27 No JSON for results](#27-results-are-not-serialized-directly) ·
  [30 Typed HTTP responses](#30-http-responses-are-typed) · [31 gRPC rich errors](#31-grpc-errors-use-the-rich-error-model) ·
  [34 Error JSON](#34-errors-have-a-fixed-json-format) ·
  [35 Telemetry](#35-failures-are-reported-through-systemdiagnostics)
- **DDD:** [28 Error catalogs](#28-error-catalogs-per-domain-concept) · [29 Who sets Field](#29-whoever-knows-the-field-name-sets-it)
- **Packages:** [36 Compatibility checked against the last release](#36-compatibility-is-checked-against-the-last-release) ·
  [37 FluentValidation failures become field errors](#37-fluentvalidation-failures-become-field-errors) ·
  [38 HTTP clients read problems back as errors](#38-http-clients-read-problems-back-as-errors) ·
  [39 Test assertions need no test framework](#39-test-assertions-need-no-test-framework) ·
  [40 Conventions are suggestions, mistakes are warnings](#40-conventions-are-suggestions-mistakes-are-warnings) ·
  [41 Result&lt;Error&gt; is an analyzer warning](#41-resulterror-is-an-analyzer-warning) ·
  [42 Results refuse to be serialized](#42-results-refuse-to-be-serialized) ·
  [43 The API review before 1.0](#43-the-api-review-before-10)

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

**Consequences.** Results stored as `IAnyResult` (for example in `Combine(IEnumerable<IAnyResult>)`) are boxed; the
XML docs say so. Structs have a `default` value, which [3](#3-a-default-result-is-a-failure) deals with.

### 3. A default result is a failure

**Context.** `default(Result<T>)` exists, through arrays, uninitialised fields or `default` literals, and must mean
something.

**Decision.** A default result is a failure carrying `Error.DefaultResult` (`result.default`). `Error` is never `null`
on a failure.

**Alternatives.** Treating `default` as "no case" would make every `switch` need a `null` arm and every caller check a
third state.

**Consequences.** The `Error` property turns the `null` backing field of a default result into `Error.DefaultResult`.
Equality does the same: both result types write their own `Equals` and `GetHashCode`, which compare the case and then
the value or the `Error` property, so `default(Result<int>) == Result<int>.Failure(Error.DefaultResult)`. Until 0.2.0
the compiler-generated record equality compared the fields, and the two were not equal; the record struct stays, so
`==`, `!=` and `Equals(object)` are still generated and call the hand-written `Equals`.

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

### 8. IAnyResult, not IResult

**Context.** The common interface was first called `IResult`. ASP.NET Core's Web SDK imports
`Microsoft.AspNetCore.Http`, whose `IResult` is the core Minimal API type, so any web project using both would hit
CS0104 (ambiguous reference). Its first published name, `IResultState`, described what it holds rather than what it is
for, and came with an `IResultState<T>` that added `ResultValue` and that no API of the library took.

**Decision.** `IAnyResult`: any result, whatever its value type, with `IsSuccess`, `IsFailure` and `Error`. It is what
`Combine(IEnumerable<IAnyResult>)` and `CombineAll` take and what `IResultFactory<TSelf>` extends. There is no generic
counterpart: code that knows the value type uses `Result<T>` itself.

**Alternatives.** `IResultInterface` repeats what the `I` says; `IOutcome` brings a second word for the same thing;
`IResultBase` is FluentResults' name. Keeping `IResultState` as an obsolete interface beside the new one would have
needed a second set of `Combine` overloads, for a package with few users yet.

**Consequences.** Renamed in 0.3.0, a binary breaking change allowed before 1.0 (`CompatibilitySuppressions.xml`
records it for package validation): `IResultState` becomes `IAnyResult`, and `IResultState<T>` is removed. Code that
only uses `Result`, `Result<T>` and `IResultFactory<TSelf>` is not affected.

### 9. IResultFactory&lt;TSelf&gt; has Failure but no Success

**Context.** CQRS pipeline behaviors (validation, authorisation, transactions) wrap handlers returning `Result`,
`Result<OrderId>`, `Result<UserDto>`… and must short-circuit with a failure of a type they only know as `TResponse`.

**Decision.** `IResultFactory<out TSelf>` with `static abstract TSelf Failure(Error)`, implemented by both result types.
It extends `IAnyResult`, so a behavior can also inspect `IsSuccess` / `Error` (for example to commit or roll back).

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

**Consequences.** Sources may be `ValueTask`: the pipeline also extends `ValueTask<Result<T>>`, `ValueTask<Result>` and
`ValueTask<T?>` (for EF Core's `FindAsync`). Those methods have the `Task` methods' names and delegates (they are
generated from them), await the source exactly once, as a `ValueTask` requires, and return `Task`, so a chain moves
to `Task` after its first step. Only the receiver is new, so no call becomes ambiguous.

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

### 32. Built with .NET 11 Runtime Async

**Context.** .NET 11 can run `async` methods natively: the runtime suspends and resumes them instead of
compiler-generated state machines. It is opt-in per project (`<Features>runtime-async=on</Features>`).

**Decision.** Every `net11.0` project in the repository, the shipped libraries included, is compiled with Runtime Async
(`Directory.Build.targets`). `-p:EnableRuntimeAsync=false` builds the classic state machines into separate
`artifacts/state-machines/` folders, for the benchmark comparison and for running the tests both ways.

**Why.** `ResultUnion.dll` shrinks from 105 KB to 58 KB, live stack traces show the real call chain, and a directly
awaited async method that completes synchronously allocates no task (see [docs/BENCHMARKS.md](BENCHMARKS.md)). The
library only targets `net11.0`, so every consumer's runtime supports it.

**Consequences.** Changing `Features` alone does not make an incremental build recompile, so the two kinds of build
must not share `bin`/`obj`. The tests run against both builds.

### 33. Async steps create no task they do not need

**Context.** A fluent chain on `Task<Result<T>>` passes a `Task` from one step to the next, so each step's task must
exist as an object. Under Runtime Async, the task of an async method that the caller awaits directly does not.

**Decision.** Steps that produce a new result stay ordinary async methods; they are already the cheapest form.
Allocation-free shortcuts are added only where they create nothing: a value-preserving step (passing `Ensure`, `Tap`,
`TapError`, `MapError` on a success) on an already-completed source returns the source task itself; successes of
`Result` share one completed task; `BindAsync` and `MatchAsync` with an async delegate return the delegate's task
instead of awaiting it inside another one. Exceptions, cancellation and `null` tasks behave exactly as in the async
methods they replace.

**Alternatives.** Running every step synchronously on a completed source and returning `Task.FromResult` was measured
first: it allocates a task that the async method would not, so the last step of a chain got more expensive. A custom
awaitable struct instead of `Task<Result<T>>` would remove the intermediate tasks, but changes every signature and the
handlers' return types.

**Consequences.** A chain's value-preserving steps now add nothing: in the benchmarks the five-step handler chain
`ToResultAsync → EnsureAsync → TapAsync → MapAsync → TapErrorAsync` allocates 256 B with Runtime Async, the same as the
three-step `ToResultAsync → BindAsync → MapAsync`. Measured in isolation, `Ensure → Tap → Map → TapError` on a
completed task went from 320–400 B to 160 B. The tasks that remain are the source and the steps that create new values.

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
An `Error` on its own does have a JSON format ([34](#34-errors-have-a-fixed-json-format)).

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

### 34. Errors have a fixed JSON format

**Context.** Between services that talk through events or queues there is no HTTP status or gRPC status to carry an
error: the error is part of the message, and the receiver should get back the same code, type, field, arguments and
details.

**Decision.** `Error` carries `[JsonConverter(typeof(ErrorJsonConverter))]`, in the core library (System.Text.Json is
part of .NET, so no dependency is added). The format is `{ code, message, type, field, arguments, details }` with fixed
names, `type` as the enum name, empty parts left out, and unknown properties skipped. Arguments are read back as JSON
values (`int`, `long`, `decimal`, `string`, `bool`, `null`, otherwise `JsonElement`). The converter rebuilds the error
exactly, through an internal factory, so a group keeps its own code.

**Alternatives.** Default serialization of the record would need a public constructor, would turn `Arguments` into
`JsonElement`s anyway, and would follow the caller's naming policy, so two services could disagree on the names. A
converter for `Result<T>` was left out for the same reasons as [27](#27-results-are-not-serialized-directly).

A separate `ResultUnion.Json` package, like `ResultUnion.AspNetCore`, was considered and rejected. ASP.NET Core is
separate because it is a large framework dependency; System.Text.Json is part of .NET, so the core library gains no
dependency. A separate package could not put `[JsonConverter]` on `Error`, so every service would have to register the
converter, and one that forgot would silently write a different format and fail to read the others'. It would also
need `InternalsVisibleTo` (and an exact version match) or a public factory to rebuild errors exactly.

**Consequences.** `Unexpected` messages are written as they are: unlike problem details and gRPC statuses, the format is
for trusted services. An argument of a type JSON does not have (a `Guid`, a date, an enum) comes back as a string, so
such an error is not equal to the original after a round trip.

### 35. Failures are reported through System.Diagnostics

**Context.** In production the useful questions are which errors happen, how often, and in which request. A result
pattern makes this easy to answer, because every expected failure has a stable code, but only if someone records it.

**Decision.** `ResultTelemetry` in the core library uses `Meter` and `Activity`, which are part of .NET, so no
OpenTelemetry package is referenced; applications enable it with `AddMeter("ResultUnion")`. `Record(operation)` /
`RecordAsync(operation)` count a failure in `resultunion.errors` (`error.type` = code, `resultunion.error.category`,
`resultunion.operation`) and add attributes and an event to the current span. The HTTP and gRPC integrations tag the
server span automatically when a failure leaves the service. The span status becomes Error only for `Unexpected` and
`Unavailable`, following the OpenTelemetry HTTP conventions, in which a 4xx is not a server error.

**Why explicit for metrics, automatic for traces.** An operation name makes the counter useful, and only the code knows
it. Span attributes are idempotent (a later one overwrites), so the hosts can set them without double counting even
when the handler also calls `Record`; a counter could not be deduplicated that way.

**Alternatives.** Recording every failure automatically, wherever a `Result` is created, would count intermediate
failures that a pipeline later handles, and would cost something on every failure even when nothing listens. Using the
OpenTelemetry API package would add a dependency to every consumer for no gain.

**Consequences.** The message and arguments are never recorded, since they may hold personal data. On spans the
attributes are namespaced (`resultunion.error.code`), so they never clash with the `error.type` that the HTTP and gRPC
instrumentations set from the status code. The RU0001 analyzer treats `Record` on a variable like `Tap`.

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

## Packages

### 36. Compatibility is checked against the last release

**Context.** Once a version is on nuget.org, code compiled against it runs against every later version a project
upgrades to, directly or through another package. The `PublicAPI` files show every public change in review, but they
are compared with the source, and nothing stops a pull request from editing them in the same commit as the change.

**Decision.** Every package enables NuGet package validation with the last released version as its baseline
(`PackageValidationBaselineVersion` in `Directory.Build.targets`). `dotnet pack`, in CI and in `publish.yml`, downloads
that version from nuget.org and fails on a binary breaking change: a removed or changed member (CP0002), a member added
to an interface (CP0006), a dependency or target framework dropped. The baseline moves to each new version right after
it is released.

**Alternatives.** The `PublicAPI` files alone catch changes at build time but compare the code with itself.
ApiCompat against a hand-kept reference assembly does the same as package validation with more files to maintain.

**Consequences.** Before 1.0 a breaking change is still allowed (CHANGELOG.md, *Changed* or *Removed*), but it has to
be recorded on purpose: `dotnet pack -p:GenerateCompatibilitySuppressionFile=true` writes `CompatibilitySuppressions.xml`
next to the project, and that file shows the break in review. Packing needs nuget.org to be reachable, because the
baseline package is downloaded on the first pack.

### 37. FluentValidation failures become field errors

**Context.** Many applications already validate requests with FluentValidation. Its `ValidationResult` is a list of
failures with a property path, an error code, a message and placeholder values; without an adapter, each application
writes the same loop to turn it into errors, and usually copies the wrong things (the attempted value) or loses the
field.

**Decision.** A separate package, `ResultUnion.FluentValidation`, like the ASP.NET Core and gRPC integrations (25), so
the core keeps no dependencies. A failure becomes a `Validation` error with the rule's `ErrorCode` as the code, its
message, the property path converted to camel case as the field (`Lines[1].Quantity` → `lines[1].quantity`, the name a
JSON client sees), and the number, string and Boolean placeholders as arguments in camel case. One failure is returned
as it is and several are grouped with `Error.Validation(details)`, the same shape as `CombineAll` (20), so problem
details and gRPC `BadRequest` report one entry per field. `ValidateToResult` / `ValidateToResultAsync` return the
validated instance, so validation is the first step of a pipeline.

**Alternatives.** Mapping FluentValidation's default codes (`NotEmptyValidator`) to ResultUnion-style codes would invent
a contract nobody chose; the package documents `WithErrorCode` instead. Copying `AttemptedValue` or every placeholder
would put user input (passwords included) and values that do not survive JSON (dates, objects) into errors that are
logged and sent to clients. Honouring `Severity` (warnings that do not fail) would disagree with `IsValid`, which counts
every failure.

**Consequences.** Field naming other than camel case is a parameter of `ToResult(fieldName)`. Asynchronous rules need
`ValidateToResultAsync`, because FluentValidation refuses to run them synchronously.

### 38. HTTP clients read problems back as errors

**Context.** `ResultUnion.AspNetCore` turns an `Error` into a problem response, and `ResultUnion.Grpc` works in both
directions, but a .NET client of an HTTP API had to parse status codes and problem JSON itself, and usually lost the code,
the arguments or the field errors on the way.

**Decision.** A separate client package, `ResultUnion.Http`, with no ASP.NET Core dependency, so Blazor WebAssembly,
MAUI and worker services can use it (25). `ReadResultAsync<T>` / `ReadResultAsync` read a response; `ReadErrorAsync`
reads the error of a failed one. The server now also sends the `ErrorType` as the `category` member: the status code
alone cannot tell `Concurrency` (transient, retry) from `Conflict`, which are both 409. With it, an error from a
ResultUnion server comes back equal to the one the server returned, except for `Unexpected`, whose message and
arguments the server never sends (26). From other servers the client reads what is there: the status decides the type,
the code is `http.<status>` (like `grpc.<status>` in 31), `detail` or `title` the message, ASP.NET Core's `errors` the
field errors, and a `Retry-After` header the `retryAfterSeconds` argument.

**Alternatives.** Putting the client in `ResultUnion.AspNetCore` would force the ASP.NET Core framework onto every
client. Catching `HttpRequestException` and timeouts inside the package would turn outages into results without the
caller choosing to; like `Result.TryAsync` (15), the client leaves that decision to the caller. Mapping every 4xx to
`Validation` would make a client bug (405, 415) look like the user's mistake, so unknown statuses are `Unexpected`.

**Consequences.** The methods on `Task<HttpResponseMessage>` dispose the response they awaited; those on
`HttpResponseMessage` leave it to the caller. A successful response without a usable body for `T` is a contract
violation and throws, as other bugs do. `category` is an addition to the problem body, so existing clients are not
affected.

### 39. Test assertions need no test framework

**Context.** Tests of code that returns results repeat the same lines: check the track, read the value or the error,
compare the code. With plain `Assert.True(result.TryGetError(out var error))`, a failing test says only "expected True",
so the reader has to debug to see which error came back.

**Decision.** A small package, `ResultUnion.Testing`. `ShouldSucceed()` returns the value and `ShouldFail()` /
`ShouldFail(code)` the error, so a test continues on the next line; `ShouldHaveCode`, `ShouldHaveType`,
`ShouldHaveField`, `ShouldHaveArgument` and `ShouldHaveFieldErrors` check an error and return it for chaining. A failed
assertion throws its own `ResultAssertionException` with the expectation and the whole error (code, message, field,
type, arguments, details), which every test framework reports as a failure, so the package depends on none.

**Alternatives.** Extensions for one framework (xUnit's `Assert`, FluentAssertions) would tie the package to it and
leave the others out. Comparing argument values with `Equals` alone would fail when a number comes back from JSON as a
`long` instead of an `int` (34), so numbers compare by value.

**Consequences.** The assertions are about what clients depend on (code, type, field, arguments), not about messages;
there is deliberately no `ShouldHaveMessage`. `ShouldHaveFieldErrors` compares the innermost errors as a set, so it
works the same for one error and for a group.

### 40. Conventions are suggestions, mistakes are warnings

**Context.** The analyzers started with mistakes that are wrong in every codebase: a discarded result (RU0001),
asynchronous code in `Result.Try` (RU0002), `Map` that should be `Bind` (RU0003), an unchecked read (RU0004). The
library's conventions (error catalogs (28), who sets `Field` (29), `Task` rather than `ValueTask` (10)) were only
written down, in the README and the skill, and code review was the only check.

**Decision.** A cancellation caught and returned as a result (RU0005) is a mistake like the first four: cancellation
is never a business failure (14), so it is a Warning. The conventions become suggestions (Info): an error created
outside a catalog (RU0006), `WithField` in a single-input value object or a catalog (RU0007), and a method returning
`ValueTask<Result…>` (RU0008). Info shows in the IDE but not in the build output and never fails a build. Each rule
recognizes its pattern by names and shapes (a type ending in `Errors`, a static factory with one simple parameter), and
leaves alone what a convention explicitly allows: `Error.Create` with a computed code (translation), a value object
with several inputs, overrides and interface implementations.

**Alternatives.** Warnings for the conventions would fail the builds of projects that organise errors differently and
turn a style into a rule. Leaving them as text would leave the skill's "quick check" to memory.

**Consequences.** The repository runs every rule: tests, the showcase, the benchmarks and the packages that translate
other systems' errors turn RU0006 off (and tests RU0008) in `.editorconfig`, with the reason next to the setting; the
samples follow the conventions.

### 41. Result&lt;Error&gt; is an analyzer warning

**Context.** In a `Result<Error>` both cases of the union are errors: `switch` on the cases cannot tell success from
failure, and an error returned as a value passes the rest of the chain as a success. It is almost never meant; it
comes from `Map` with a mapper that returns an error, where `Bind`, `Ensure` or `MapError` was meant.

**Decision.** RU0009 (Warning) reports it where the type is written and where a call produces it from something that
was not one (`Map(x => error)`, a generic method closed over `Error`); the following steps of the same chain are not
reported again.

**Alternatives.** C# has no constraint that excludes a type (`where T : not Error`). A check in a static constructor of
`Result<T>` would fail only at run time, with a `TypeInitializationException`, and would cost every static member of
every `Result<T>` the check that the type is initialised (a generic struct then loses `beforefieldinit`).

**Consequences.** Generic code (`Result<T>` with any `T`) is not reported, only its use with `Error`. Types that
contain errors, such as `Result<Error[]>`, are fine.

### 42. Results refuse to be serialized

**Context.** A result is mapped where it leaves the application (`ToHttpResult`, `GetValueOrThrowRpc`, a DTO); it is
not a JSON contract. Until 0.3.0 nothing enforced that, and System.Text.Json in .NET 11 treats `Result<T>` as a union
and writes its case: a Minimal API endpoint that returned a result sent a failure as `200 OK` with the error as the
body, and a successful `Result` failed with "Cannot access 'Error' when the 'Result' is a success".

**Decision.** Both result types carry `[JsonConverter]` with an internal converter that throws
`NotSupportedException` on write and on read, with a message that says what to do instead. Analyzer RU0010 (Warning)
reports the same mistakes at build time: a Minimal API handler that returns a result, and a result passed to or read
by the JSON methods of System.Text.Json, ASP.NET Core and System.Net.Http.Json. `Error` keeps its own converter and
stays serializable.

**Alternatives.** The analyzer alone would miss a result passed on as `object`; the converter alone would show the
mistake only when the endpoint is called. A JSON format for results (`{ "isSuccess": …, "value": … }`) would make them
a contract between services, which `Error` already is on its own. The converter for `Result<T>` is an open generic in
the attribute, which System.Text.Json supports from .NET 11 (on .NET 10 it fails with `ContainsGenericParameters`);
it keeps `MakeGenericType` out of the AOT path.

**Consequences.** A behavior change in a released type: code that serialized a result, on purpose or not, now throws.
Before, it produced JSON that no reader of the library could turn back into a result.

### 43. The API review before 1.0

**Context.** After 0.4.0 the six packages had about 570 public members. A break costs least before 1.0, so every
member was read for names, overload sets and consistency between packages.

**Decision.** Three names change in 0.5.0. `ErrorType.IsTransient` becomes a property, like `Error.IsTransient`:
the two read the same now. A method and a property of one name cannot coexist on one type, so this one is a clean
break. `ErrorType.ToStatusCode()` becomes `ToHttpStatusCode()`, which says which status it is next to ResultUnion.Grpc's
`ToGrpcStatusCode()`. On a `Result` without a value, `GetValueOrThrowRpc(onSuccess)` becomes
`ToReplyOrThrowRpc(onSuccess)`: there is no value to get, the reply comes from `onSuccess`. `Result<T>` keeps
`GetValueOrThrowRpc()`, which does return its value. The two renamed members stay until 1.0 as `[Obsolete]`
forwarders hidden from IntelliSense, so 0.5.0 breaks only `IsTransient()`.

**Alternatives.** Kept as they are, after the review: `Combine` (stops at the first failure) and `CombineAll` (reports
every failure), whose difference is recorded in [20](#20-combineall-semantics); the three meanings of
`ToResult` (`Result<T>` to `Result`, `ValidationResult` to `Result`, `T?` to `Result<T>`), which all turn something into
a result; the public `ProblemErrorDetail`, `ErrorJsonConverter` and constants, which clients and source-generated JSON
contexts need. `ValueTask` sources for `RecordAsync`, `ToHttpResultAsync` and `GetValueOrThrowRpcAsync` were left out:
the first pipeline step turns a `ValueTask` into a `Task`, so they would rarely be reached; they can be added without a
break.

**Consequences.** 1.0 removes `ToStatusCode()` and the two `GetValueOrThrowRpc(onSuccess)` overloads.
