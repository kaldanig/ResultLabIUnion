# Benchmarks

Time and allocations of ResultUnion, measured with BenchmarkDotNet. The benchmarks are in
[`ResultUnion.Benchmarks`](https://github.com/kaldanig/ResultLabIUnion/tree/master/ResultUnion.Benchmarks); every number below is from one run of the full suite, on 2026-11-23.

| | |
|---|---|
| Runtime | .NET 11.0.0, X64 RyuJIT x86-64-v4, projects built with Runtime Async |
| Machine | AMD Ryzen 9 7950X3D, Windows 11 |
| Tool | BenchmarkDotNet 0.16.0-preview.2, out-of-process, default job, memory diagnoser |

Run them yourself (about 20 minutes on this machine; `--filter '*Async*'` for one class):

```powershell
dotnet run -c Release --project ResultUnion.Benchmarks -- --filter '*'
```

BenchmarkDotNet 0.15.8, the latest stable release, knows runtimes only up to .NET 10: out-of-process on `net11.0` it
fails with `GetRuntimeVersion not implemented for NotRecognized`. 0.16 adds .NET 11, so the benchmark project uses the
preview. Nothing else depends on it.

### In CI

`benchmarks.yml` has two jobs:

- **smoke**, on every push to master and pull request that changes the library or the benchmarks: every benchmark
  runs once (`--smoke`: one launch, one iteration, no warm-up), in about a minute. It fails when a benchmark throws or
  a class cannot run, which the build alone does not catch. Its numbers mean nothing.
- **full**, every Monday and on demand (**Actions → Benchmarks → Run workflow**, with an optional filter): the measured
  suite. The reports are in the run summary and, with the CSV and HTML files, in the `benchmarks` artifact. A shared
  runner is noisy, so compare its times only for large changes; the allocations are exact. It never blocks anything.

The smoke run locally:

```powershell
dotnet run -c Release --project ResultUnion.Benchmarks -- --filter '*' --smoke
```

BenchmarkDotNet exits with 0 even when a benchmark fails; the project returns 1 then, and names the class.

## Summary

- **The success path allocates nothing.** Matching, pipelines and `CombineAll` without failures: 0 B.
- **A failure costs nanoseconds, an exception microseconds.** A cached catalog error is about 350× cheaper than
  `throw` + `catch`.
- **A pipeline costs a few nanoseconds more than `if` code:** 9–10 ns against 5–6 ns for four steps.
- **Against other libraries, ResultUnion and ErrorOr are in the same range, and FluentResults and exceptions are
  not.** For one use case with every failure reported: 15–69 ns for ResultUnion, 10–33 ns for ErrorOr, 113–383 ns
  for FluentResults (which allocates on success too), and about 850–890 ns for an exception at the first failure.
- **A failure that crosses the wire costs up to about two microseconds on each side.** Writing an RFC 9457 problem as JSON
  takes 1.1–2.3 µs and a client reads it back in 0.7–1.7 µs; the gRPC status and its reading take 0.5–1.3 µs. Each
  allocates a few KB per failure, with every field reported; a success pays nothing.
- **Async chains allocate a task per step that creates a value.** With Runtime Async the plain `async` baseline
  allocates nothing, so a chain's 256 B is the library's own cost: small next to any real I/O (a database call is
  100,000+ ns), but visible in a tight loop.

## Reading a result

| Benchmark | Mean | Allocated |
|---|---:|---:|
| `switch` on the union (success) | 1.4 ns | 0 B |
| `switch` on the union (failure) | 1.1 ns | 0 B |
| `TryGetResultValue` / `TryGetError` | 1.0–1.2 ns | 0 B |
| `IsSuccess` + `ResultValue` / `Error` | 1.2 ns | 0 B |
| `Match(onSuccess, onFailure)` | 1.6–1.7 ns | 0 B |
| `Value is int` (the union's `object?` property) | 1.8–2.1 ns | 0 B |

`Value` is typed `object?`, so it boxes an `int` in principle; here the JIT proved the box does not escape and
removed it. That is an optimization, not a guarantee: `switch`, `Match` and `TryGetResultValue` never box.

## A pipeline against `if` code

The same four-step validation (not empty, parse, range check, double), written with early returns and as
`Ensure → Bind → Ensure → Map → Match` with static lambdas:

| Input | Imperative | Pipeline | Allocated |
|---|---:|---:|---:|
| `"42"` (success) | 5.7 ns | 9.6 ns | 0 B |
| `"abc"` (failure) | 5.1 ns | 9.2 ns | 0 B |

The difference is the delegate calls and struct copies: a few nanoseconds for four steps, no allocations.

## A failure against an exception

| Benchmark | Mean | Allocated |
|---|---:|---:|
| Return a cached catalog error (`Email.Errors.Required`) | 2.7 ns | 0 B |
| Return a new error with `WithArgument` | 29 ns | 320 B |
| `throw` and `catch` an exception | 965 ns | 376 B |

Catalog errors without values are `static readonly` and cost nothing. An error with arguments builds a small
dictionary; it is still about 30× cheaper than an exception.

## CombineAll

`Result.CombineAll(a, b, c).Map((x, y, z) => …)`:

| Failures | Mean | Allocated |
|---|---:|---:|
| 0 | 13 ns | 0 B |
| 1 | 14 ns | 88 B |
| 3 | 34 ns | 224 B |

Failures allocate the grouped error (`Details`). The first measurement showed 24 B on the success path: the tuple
`Map` and `Bind` wrapped the delegate in a lambda, which allocated a closure on every call. They now read the tuple
directly.

## Against other libraries

The same use case written four times, each the way its library's documentation writes it: an email (needs an `@`)
and a password (8 characters) validated together, with every failure reported. ResultUnion uses `CombineAll` and
catalog errors (the email's with its value as an argument), FluentResults `Result.Merge`, ErrorOr a list of its
`Error` structs, and the exception version throws at the first failure, since an exception cannot report both. The code is
`ComparisonBenchmarks.cs`; FluentResults 4.0.0 and ErrorOr 2.1.1.

| Input | ResultUnion | ErrorOr | FluentResults | Exceptions |
|---|---:|---:|---:|---:|
| valid | 16 ns, 80 B | 10 ns, 80 B | 113 ns, 848 B | 1.7 ns, 0 B |
| one invalid (the password) | 15 ns, 112 B | 20 ns, 200 B | 280 ns, 1,640 B | 847 ns, 424 B |
| two invalid | 69 ns, 632 B | 33 ns, 392 B | 383 ns, 2,280 B | 888 ns, 496 B |

How to read it:

- **Valid input.** The 80 B in ResultUnion and ErrorOr are the three records the use case creates (the email, the
  password and the account); the libraries add nothing. The exception version creates no value objects and the JIT
  removed its one allocation, so its 1.7 ns is the cost of two checks: the price of the Result pattern on success is
  the 10–15 ns of building and combining results. FluentResults allocates its result objects and error lists on
  success too.
- **One failure.** A cached catalog error (`static readonly`) is free, so ResultUnion's failure costs about what its
  success does; ErrorOr builds a list for the error.
- **Two failures.** ResultUnion builds the email error with its argument (a dictionary: about 30 ns and 320 B on its
  own, see [A failure against an exception](#a-failure-against-an-exception)) and a grouped error for the two. ErrorOr's
  errors here carry no values. The argument is what lets a client translate the message
  ([Translating error messages](articles/localization.md)), and its dictionary accounts for most of the 36 ns
  between the two.
- **Exceptions** cost about 850–890 ns per failure whatever the input, 13 to 57 times the results, and report only
  the first failure.

None of these numbers matter next to a database call (100,000 ns and more). They matter in a loop that validates many
values, and there the difference that counts is between results of either kind and exceptions.

## HTTP errors

A failure that leaves a Minimal API endpoint becomes an RFC 9457 problem (`ToHttpResult`), written as JSON with the
error's code, arguments and, for validation, every field; ResultUnion.Http reads it back on the client
(`ReadResultAsync`). The code is `HttpMappingBenchmarks.cs`.

| Failure | `ToHttpResult` | written as JSON (endpoint) | `ReadResultAsync` (client) |
|---|---:|---:|---:|
| not found, one argument | 57 ns, 304 B | 1,094 ns, 2.9 KB | 734 ns, 1.6 KB |
| two invalid fields, with arguments | 353 ns, 1.8 KB | 2,271 ns, 4.8 KB | 1,707 ns, 3.2 KB |

`ToHttpResult` only builds the `ProblemDetails`; writing it, with `System.Text.Json` into the response body, is most of
the cost. The client column includes creating the `HttpResponseMessage` and its content, which a real call has anyway.
The same error costs about the same as over gRPC: the work is in the serialization, whichever format it is.

## gRPC errors

A failure that leaves a gRPC service becomes a rich status (`google.rpc.Status` with an `ErrorInfo` and, for
validation, a `BadRequest`), serialized into the call's trailers, and `ToError()` reads it back on the client. The code
is `GrpcMappingBenchmarks.cs`.

| Failure | `ToRpcStatus` | `ToRpcException` (service) | `ToError` (client) |
|---|---:|---:|---:|
| not found, one argument | 370 ns, 1.3 KB | 605 ns, 2.2 KB | 528 ns, 2.6 KB |
| two invalid fields, with arguments | 919 ns, 3.1 KB | 1,258 ns, 4.5 KB | 1,042 ns, 5.0 KB |

`ToRpcException` is what `GetValueOrThrowRpcAsync` does with a failure: the status and its serialization into the
trailers. The cost grows with what the error carries, since every argument and field travels in the details, and it is
paid only for a failure. Next to the call itself, a network round trip with HTTP/2 framing, it is small; it matters
only for a service that fails most of its calls in a tight loop.

## Async chains, Runtime Async against state machines

A chain whose I/O completes synchronously (a cache hit), so only the library's overhead is measured. The
`StateMachines` job builds everything, ResultUnion included, with `-p:EnableRuntimeAsync=false`.

| Benchmark | Runtime Async | State machines |
|---|---:|---:|
| Hand-written `async` with `if` checks | 2.3 ns, 0 B | 18 ns, 216 B |
| `ToResultAsync → BindAsync → MapAsync` | 43 ns, 256 B | 53 ns, 456 B |
| `ToResultAsync → EnsureAsync → TapAsync → MapAsync → TapErrorAsync` | 58 ns, 256 B | 54 ns, 304 B |

With Runtime Async, an async method that the caller awaits directly does not create its task, which is why the
hand-written baseline allocates nothing. A fluent chain cannot avoid that: each step's task is the next step's source,
so it must exist. What the library does about it ([DESIGN.md, decision 33](DESIGN.md#33-async-steps-create-no-task-they-do-not-need)):

- a step that leaves the result unchanged (`EnsureAsync` that passes, `TapAsync`, `TapErrorAsync`, `MapErrorAsync` on
  a success) returns its source task when the source has already completed, so the five-step handler chain allocates
  no more than the three-step one;
- every success of a `Result` without a value shares one completed task;
- `BindAsync` and `MatchAsync` return the async delegate's own task instead of wrapping it.

Measured in isolation before and after these changes (bytes per call, source task included):

| Chain on a completed task | Caller with state machines | Caller with Runtime Async |
|---|---:|---:|
| `EnsureAsync → TapAsync → MapAsync → TapErrorAsync` | 400 → 160 B | 320 → 160 B |
| `BindAsync(async) → TapAsync(async)` | 320 → 240 B | 240 → 160 B |
| `ToResultAsync → BindAsync(async) → MapAsync` | 536 → 456 B | 384 → 304 B |

When it matters (a hot loop over cached data), await once and continue with the synchronous methods:
`var user = await repository.FindAsync(id, ct).ToResultAsync(error);` then `user.Ensure(…).Map(…)`.
