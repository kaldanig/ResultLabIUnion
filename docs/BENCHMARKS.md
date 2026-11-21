# Benchmarks

Time and allocations of ResultUnion, measured with BenchmarkDotNet. The benchmarks are in
[`ResultUnion.Benchmarks`](https://github.com/kaldanig/ResultLabIUnion/tree/master/ResultUnion.Benchmarks); every number below is from one run of the full suite.

| | |
|---|---|
| Runtime | .NET 11.0.0, X64 RyuJIT x86-64-v4, projects built with Runtime Async |
| Machine | AMD Ryzen 9 7950X3D, Windows 11 |
| Tool | BenchmarkDotNet 0.16.0-preview.2, out-of-process, default job, memory diagnoser |

Run them yourself (about 25 minutes; `--filter '*Async*'` for one class):

```powershell
dotnet run -c Release --project ResultUnion.Benchmarks -- --filter '*'
```

BenchmarkDotNet 0.15.8, the latest stable release, knows runtimes only up to .NET 10: out-of-process on `net11.0` it
fails with `GetRuntimeVersion not implemented for NotRecognized`. 0.16 adds .NET 11, so the benchmark project uses the
preview. Nothing else depends on it.

## Summary

- **The success path allocates nothing.** Matching, pipelines and `CombineAll` without failures: 0 B.
- **A failure costs nanoseconds, an exception microseconds.** A cached catalog error is about 300× cheaper than
  `throw` + `catch`.
- **A pipeline costs a few nanoseconds more than `if` code:** 10–13 ns against 7–8 ns for four steps.
- **Async chains allocate a task per step that creates a value.** With Runtime Async the plain `async` baseline
  allocates nothing, so a chain's 256 B is the library's own cost: small next to any real I/O (a database call is
  100,000+ ns), but visible in a tight loop.

## Reading a result

| Benchmark | Mean | Allocated |
|---|---:|---:|
| `switch` on the union (success) | 1.7 ns | 0 B |
| `switch` on the union (failure) | 1.4 ns | 0 B |
| `TryGetResultValue` / `TryGetError` | 1.3–1.5 ns | 0 B |
| `IsSuccess` + `ResultValue` / `Error` | 1.5–1.7 ns | 0 B |
| `Match(onSuccess, onFailure)` | 2.4 ns | 0 B |
| `Value is int` (the union's `object?` property) | 2.6–2.8 ns | 0 B |

`Value` is typed `object?`, so it boxes an `int` in principle; here the JIT proved the box does not escape and
removed it. That is an optimization, not a guarantee: `switch`, `Match` and `TryGetResultValue` never box.

## A pipeline against `if` code

The same four-step validation (not empty, parse, range check, double), written with early returns and as
`Ensure → Bind → Ensure → Map → Match` with static lambdas:

| Input | Imperative | Pipeline | Allocated |
|---|---:|---:|---:|
| `"42"` (success) | 7.9 ns | 12.5 ns | 0 B |
| `"abc"` (failure) | 7.0 ns | 10.4 ns | 0 B |

The difference is the delegate calls and struct copies: a few nanoseconds for four steps, no allocations.

## A failure against an exception

| Benchmark | Mean | Allocated |
|---|---:|---:|
| Return a cached catalog error (`Email.Errors.Required`) | 4.0 ns | 0 B |
| Return a new error with `WithArgument` | 48 ns | 320 B |
| `throw` and `catch` an exception | 1,239 ns | 376 B |

Catalog errors without values are `static readonly` and cost nothing. An error with arguments builds a small
dictionary; it is still about 25× cheaper than an exception.

## CombineAll

`Result.CombineAll(a, b, c).Map((x, y, z) => …)`:

| Failures | Mean | Allocated |
|---|---:|---:|
| 0 | 14 ns | 0 B |
| 1 | 21 ns | 88 B |
| 3 | 50 ns | 224 B |

Failures allocate the grouped error (`Details`). The first measurement showed 24 B on the success path: the tuple
`Map` and `Bind` wrapped the delegate in a lambda, which allocated a closure on every call. They now read the tuple
directly.

## Async chains, Runtime Async against state machines

A chain whose I/O completes synchronously (a cache hit), so only the library's overhead is measured. The
`StateMachines` job builds everything, ResultUnion included, with `-p:EnableRuntimeAsync=false`.

| Benchmark | Runtime Async | State machines |
|---|---:|---:|
| Hand-written `async` with `if` checks | 2.3 ns, 0 B | 17.9 ns, 216 B |
| `ToResultAsync → BindAsync → MapAsync` | 63 ns, 256 B | 73 ns, 456 B |
| `ToResultAsync → EnsureAsync → TapAsync → MapAsync → TapErrorAsync` | 66 ns, 256 B | 75 ns, 304 B |

With Runtime Async, an async method that the caller awaits directly does not create its task, which is why the
hand-written baseline allocates nothing. A fluent chain cannot avoid that: each step's task is the next step's source,
so it must exist. What the library does about it ([DESIGN.md, decision 33](DESIGN.md#33-async-steps-create-no-task-they-do-not-need)):

- a step that leaves the result unchanged (`EnsureAsync` that passes, `TapAsync`, `TapErrorAsync`, `MapErrorAsync` on
  a success) returns its source task when the source has already completed, so the five-step handler chain costs the
  same as the three-step one;
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
