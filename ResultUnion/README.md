# ResultUnion

A Result pattern for .NET built on **C# 15 unions**. An operation returns `Result<T>` (a value or an `Error`) or
`Result` (success or an `Error`); failures are part of the signature, and a pipeline runs only while every step
succeeds.

```csharp
public Task<Result<User>> RegisterAsync(RegisterUser command, CancellationToken ct) =>
    Result.CombineAll(                                                   // every invalid field at once
            Email.Create(command.Email).MapError(e => e.WithField("email")),
            Password.Create(command.Password).MapError(e => e.WithField("password")))
        .Bind((email, password) => User.Create(email, password))
        .EnsureAsync(user => users.EnsureEmailIsFreeAsync(user.Email, ct))
        .TapAsync(user => users.AddAsync(user, ct));

var message = await RegisterAsync(command, ct) switch
{
    User user   => $"registered {user.Email}",
    Error error => $"{error.Code}: {error.Message}",
};
```

- `Map`, `Bind`, `Ensure`, `Tap`, `TapError`, `MapError`, `Match`, with `…Async` forms on `Task<Result<T>>`
- `Error` with a stable `Code`, an `ErrorType` (Validation, NotFound, Conflict, BusinessRule, TooManyRequests,
  Unavailable, …), `Field`, nested `Details` and `Arguments` for translated messages
- `Result.CombineAll` for tuples and collections (`lines[2].quantity`), `Result.Try` / `TryAsync` for exceptions,
  `FromNullable` / `ToResultAsync` for lookups
- `IResultFactory<TSelf>` for generic pipeline behaviors
- Analyzers included: a discarded `Result` (RU0001), `async` code in `Result.Try` (RU0002), `Map` that should be
  `Bind` (RU0003)

Requires `net11.0` (C# 15). Integrations: **ResultUnion.AspNetCore** (typed Minimal API results, problem details) and
**ResultUnion.Grpc** (gRPC rich error model).

Documentation and design notes: https://github.com/kaldanig/ResultLabIUnion
