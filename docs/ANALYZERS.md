# ResultUnion analyzers

The `ResultUnion` package ships Roslyn analyzers that catch the mistakes the type system cannot. They run in the
compiler and the IDE as soon as a project references the package; there is nothing else to install.

| Rule | Severity | What it finds | Code fix |
|---|---|---|---|
| [RU0001](#ru0001) | Warning | A `Result` is discarded | `_ = …` |
| [RU0002](#ru0002) | Warning | Asynchronous code passed to `Result.Try` | `Result.TryAsync`, awaited in an `async` function |
| [RU0003](#ru0003) | Warning | `Map` with a mapper that returns a `Result` | `Bind` / `BindAsync` |

Each rule has a code fix (the light bulb in Visual Studio and Rider, with Fix All for a document, project or solution). From the command line, `dotnet format analyzers --diagnostics RU0003 --severity warn` applies one; run one rule per call, because a fix for one rule moves the positions the next rule's diagnostics refer to.

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

**Code fix:** prefixes the call with `_ =`, also after `await` and in an expression-bodied lambda.

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

**Code fix:** renames the call to `Result.TryAsync`. Inside an `async` function it also adds `await` (in parentheses when the result is used further: `(await Result.TryAsync(…)).Match(…)`), so the code keeps compiling; elsewhere the call now returns a `Task` to await.

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

**Code fix:** renames `Map` to `Bind` and `MapAsync` to `BindAsync`, dropping explicit type arguments (the ones written for `Map` would be wrong for `Bind`).
