# Changelog

All notable changes to the ResultUnion packages are listed here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow [Semantic Versioning](https://semver.org/).
Before 1.0, a minor version may contain breaking changes; they are listed under **Changed** or **Removed**.

The public API of each package is recorded in its `PublicAPI.Shipped.txt`, so the exact changes between two versions
are in the diff of those files.

## [Unreleased]

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

[Unreleased]: https://github.com/kaldanig/ResultLabIUnion/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/kaldanig/ResultLabIUnion/releases/tag/v0.1.0
