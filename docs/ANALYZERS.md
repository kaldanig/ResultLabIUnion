# ResultUnion analyzers

The `ResultUnion` package ships Roslyn analyzers that catch the mistakes the type system cannot. They run in the
compiler and the IDE as soon as a project references the package; there is nothing else to install.

| Rule | Severity | What it finds |
|---|---|---|
| [RU0001](#ru0001) | Warning | A `Result` is discarded |
| [RU0002](#ru0002) | Warning | Asynchronous code passed to `Result.Try` |
| [RU0003](#ru0003) | Warning | `Map` with a mapper that returns a `Result` |

Change a rule's severity in `.editorconfig`, for example `dotnet_diagnostic.RU0001.severity = error`, or turn it off
with `none`.

## RU0001

**A `Result` is discarded.** A failure is an ordinary return value, so a call whose result is not used loses the error
silently: the code carries on as if it had succeeded.

```csharp
users.Deactivate(email);                 // RU0001: the Result is discarded
await unitOfWork.SaveChangesAsync(ct);   // RU0001: the awaited Result is discarded
```

Use the result: return it, chain it (`Bind`, `Ensure`), or handle it (`Match`, `switch`, `TryGetError`). When ignoring
it is really intended, say so with a discard:

```csharp
_ = cache.Refresh(key);                  // best effort: a failure is acceptable here
```

Reported for calls that return `Result`, `Result<T>`, or a `Task`/`ValueTask` of one, whether awaited or not, including
inside a lambda whose delegate returns nothing. Not reported for `Tap` / `TapError` / `Record` (and their async forms)
called on a variable, parameter, field or property: the result they return is the one the variable already holds.

## RU0002

**Asynchronous code passed to `Result.Try`.** `Result.Try` runs its delegate synchronously. An `async` lambda becomes a
`Task` value (with `Func<T>`) or an `async void` method (with `Action`), so only exceptions thrown before its first
`await` are caught; later ones escape or are lost.

```csharp
Result.Try(async () => await client.GetAsync(url), ex => Error.Unavailable(ex.Message));     // RU0002
await Result.TryAsync(() => client.GetAsync(url), ex => Error.Unavailable(ex.Message));      // awaits the task
```

Reported for an `async` lambda or a method group that returns a `Task` or `ValueTask`, for every `Result.Try`
overload (also the typed `(SomeException ex) => …` form).

## RU0003

**`Map` with a mapper that returns a `Result`.** `Map` is for steps that cannot fail (`T` → `U`). A step that returns a
result produces `Result<Result<U>>`: the inner failure is wrapped in a success, and the rest of the chain never sees it.
`Bind` flattens it.

```csharp
input.Map(s => Email.Create(s));          // RU0003: Result<Result<Email>>
input.Bind(s => Email.Create(s));         // Result<Email>
```

Reported for `Map` and `MapAsync` on `Result<T>`, on `Task<Result<T>>` and on the tuples from `CombineAll`; the
message names the replacement (`Bind` or `BindAsync`).
