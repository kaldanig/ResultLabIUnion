# ResultUnion analyzers

The `ResultUnion` package ships Roslyn analyzers that catch the mistakes the type system cannot. They run in the
compiler and the IDE as soon as a project references the package; there is nothing else to install.

| Rule | Severity | What it finds | Code fix |
|---|---|---|---|
| [RU0001](#ru0001) | Warning | A `Result` is discarded | `_ = …` |
| [RU0002](#ru0002) | Warning | Asynchronous code passed to `Result.Try` | `Result.TryAsync`, awaited in an `async` function |
| [RU0003](#ru0003) | Warning | `Map` with a mapper that returns a `Result` | `Bind` / `BindAsync` |
| [RU0004](#ru0004) | Warning | `ResultValue` or `Error` read without checking the result | none |

Rules RU0001–RU0003 have a code fix (the light bulb in Visual Studio and Rider, with Fix All for a document, project or solution). From the command line, `dotnet format analyzers --diagnostics RU0003 --severity warn` applies one; run one rule per call, because a fix for one rule moves the positions the next rule's diagnostics refer to.

The fixes are covered by tests that apply them with Roslyn's workspace API (`ResultUnion.Analyzers.Tests`), and were
checked by hand in Visual Studio 2026 with the published `ResultUnion` 0.1.0 package on 2026-11-22: every fix in the
table above gave the expected code in the preview and after applying it, Fix All in a document fixed every occurrence,
comments next to the fixed code stayed in place, and RU0004 offered no fix.

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

## RU0004

**`ResultValue` or `Error` read without checking the result.** `ResultValue` throws on a failure and `Error` throws on a
success, so reading one where the state is not known is an exception waiting for the wrong input.

```csharp
var user = users.Find(email);
return user.ResultValue.Name;                       // RU0004: throws when the user was not found

if (user.TryGetError(out var error)) return error;  // or: if (user.IsFailure) return user.Error;
return user.ResultValue.Name;                       // fine: the state is known here
```

Prefer `switch`, `Match` or `TryGetResultValue` / `TryGetError`, which need no check at all. A read is accepted when, for
the same result (the same local, parameter, field or property, or `this`):

- it is inside `if (r.IsSuccess)` / `if (r.IsFailure)` (or `!`, `== false`, the `else` branch), or on the matching
  side of `?:`, `&&` or `||`;
- an earlier `if` in the same block exits when the state is wrong: `if (r.IsFailure) return …;`,
  `if (!r.IsSuccess) throw …;`;
- the check is `r.TryGetResultValue(out …)` or `r.TryGetError(out …)`.

A read on a call's result, `Parse(s).ResultValue`, is always reported: it cannot have been checked. The rule is local and
does not follow reassignments, loops or `switch` statements, so it may report a read that another construct makes safe;
rewrite it with `switch`, `Match` or `TryGetResultValue`, or suppress it where the state is known by construction.

Tests read values directly on results whose state they know; turn the rule off for them in `.editorconfig`:

```ini
[*Tests/**.cs]
dotnet_diagnostic.RU0004.severity = none
```

No code fix: the right replacement (return, throw, `Match`, a default) depends on what the code should do on the other
track.
