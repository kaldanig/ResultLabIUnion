---
_layout: landing
---

# ResultUnion

The **Result pattern** for .NET 11, built on **C# 15 unions**. An operation returns `Result<T>`: either the value or an
`Error`. Failures are part of the signature instead of exceptions, and a pipeline runs only while every step succeeds.

```csharp
public Result<User> Register(string? email, string? age) =>
    Email.Create(email)
        .Ensure(repository.EnsureEmailIsFree)
        .Bind(e => User.ParseAge(age).Map(a => new User(Guid.NewGuid(), e, a)))
        .Ensure(u => u.Age >= 18, u => Error.BusinessRule($"{u.Email} is {u.Age}."))
        .Tap(repository.Add);
```

| Package | Contents |
|---|---|
| `ResultUnion` | `Result<T>`, `Result`, `Error`, the async pipeline, JSON for errors, OpenTelemetry metrics, and the analyzers RU0001–RU0010 with their code fixes |
| `ResultUnion.AspNetCore` | Results as HTTP responses: status codes, RFC 9457 problem details, OpenAPI error responses |
| `ResultUnion.Grpc` | Results as gRPC statuses with the `google.rpc` rich error model, and `RpcException` back to `Error` |
| `ResultUnion.FluentValidation` | FluentValidation results as `Result`, with one field error per failure |
| `ResultUnion.Http` | HTTP responses read back as `Result`, problem details as `Error`: the client side of `ResultUnion.AspNetCore` |
| `ResultUnion.Testing` | Test assertions: `ShouldSucceed()`, `ShouldFail("code")`, `ShouldHaveFieldErrors(...)` |
| `ResultUnion.Templates` | `dotnet new resultunion-api`: a Minimal API and its tests, following the conventions |

## Where to go next

- [Getting started](../articles/getting-started.md): one feature from a value object to an HTTP endpoint, in fifteen
  minutes.
- [Guide](../../README.md): the types, pipelines, async, ASP.NET Core, gRPC, JSON, OpenTelemetry and DDD, with examples.
- Articles: [EF Core](../articles/ef-core.md), [pipeline behaviors](../articles/pipeline-behaviors.md) and
  [testing](../articles/testing.md).
- [Design decisions](../DESIGN.md): why the library is built this way, with the alternatives that were rejected.
- [Analyzers](../ANALYZERS.md): the mistakes the compiler catches for you, and their code fixes.
- [Benchmarks](../BENCHMARKS.md): time and allocations against exceptions.
- [API reference](../../artifacts/docs/api/ResultUnion.yml): every public type and member of the three packages.
- [Changelog](../../CHANGELOG.md) and [releasing](../RELEASING.md).

The source is on [GitHub](https://github.com/kaldanig/ResultLabIUnion), under the MIT license.
