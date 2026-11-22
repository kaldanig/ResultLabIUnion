# ResultUnion analyzers

The `ResultUnion` package ships Roslyn analyzers that catch the mistakes the type system cannot. They run in the
compiler and the IDE as soon as a project references the package; there is nothing else to install.

| Rule | Severity | What it finds | Code fix |
|---|---|---|---|
| [RU0001](#ru0001) | Warning | A `Result` is discarded | `_ = …` |
| [RU0002](#ru0002) | Warning | Asynchronous code passed to `Result.Try` | `Result.TryAsync`, awaited in an `async` function |
| [RU0003](#ru0003) | Warning | `Map` with a mapper that returns a `Result` | `Bind` / `BindAsync` |
| [RU0004](#ru0004) | Warning | `ResultValue` or `Error` read without checking the result | none |
| [RU0005](#ru0005) | Warning | Cancellation turned into a failure | `when (!ct.IsCancellationRequested)` |
| [RU0006](#ru0006) | Info | An error created outside an error catalog | none |
| [RU0007](#ru0007) | Info | `WithField` where the field name is not known | none |
| [RU0008](#ru0008) | Info | A method that returns a `ValueTask` of a result | none |
| [RU0009](#ru0009) | Warning | `Result<Error>`, an error as a success value | `Map` → `Bind` with a failure |
| [RU0010](#ru0010) | Warning | A `Result` written as or read from JSON | `.ToHttpResult()` on an endpoint lambda |

Rules RU0001–RU0003, RU0005, RU0009 and RU0010 have a code fix (the light bulb in Visual Studio and Rider, with Fix All for a document, project or solution). From the command line, `dotnet format analyzers --diagnostics RU0003 --severity warn` applies one; run one rule per call, because a fix for one rule moves the positions the next rule's diagnostics refer to.

The fixes are covered by tests that apply them with Roslyn's workspace API (`ResultUnion.Analyzers.Tests`), and were
checked by hand in Visual Studio 2026 with the published `ResultUnion` 0.1.0 package on 2026-11-22: every fix in the
table above gave the expected code in the preview and after applying it, Fix All in a document fixed every occurrence,
comments next to the fixed code stayed in place, and RU0004 offered no fix.

RU0006–RU0008 are suggestions (Info): they show in the IDE, not in the build output, and rely on naming conventions, so
turn off any that does not fit your code. Change a rule's severity in `.editorconfig`, for example `dotnet_diagnostic.RU0001.severity = error`, or turn it off
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

## RU0005

**Cancellation turned into a failure.** A `catch` of `OperationCanceledException` (or `TaskCanceledException`) without
a `when` filter that returns a result swallows the cancellation: the caller asked to stop, and gets a failure that
looks like a business outcome, or even a success.

```csharp
try { return await client.GetFromJsonAsync<Rate>(url, ct); }
catch (OperationCanceledException) { return Errors.Unavailable; }    // RU0005

try { return await client.GetFromJsonAsync<Rate>(url, ct); }
catch (TaskCanceledException) when (!ct.IsCancellationRequested)     // fine: HttpClient's timeout, not a cancellation
{
    return Errors.Unavailable;
}
```

Let cancellation propagate, as `Result.Try` and `Result.TryAsync` do. HttpClient reports its own timeout as a
`TaskCanceledException`; a `when (!cancellationToken.IsCancellationRequested)` filter tells it apart and is not
reported. A fallback value counts too: `return -1;` in a method that returns `Result<int>` becomes a success. A catch
that rethrows, or returns something that does not become a result or an error, is not reported.

**Code fix:** when a `CancellationToken` parameter or local is in scope, the catch gets the filter
`when (!ct.IsCancellationRequested)`, one fix per token: a cancellation requested through that token propagates, and
only a timeout is still turned into a result. Without a token in scope there is no fix, because whether to remove the
catch or rethrow depends on what the code is for. Fix All is not offered: which token to use is a choice per catch.

## RU0006

**An error created outside an error catalog.** Error codes are a contract: clients switch on them and translate them.
Each code should have one definition, in the catalog of the concept it is about.

```csharp
public Result Check(Order order) =>
    order.Lines.Count > 0 ? Result.Success() : Error.Validation("No lines.");        // RU0006

public sealed partial class Order
{
    public static class Errors
    {
        public static readonly Error NoLines =
            Error.Create("order.no_lines", "An order needs at least one line.", ErrorType.Validation);
    }
}
```

Reported for `Error.Create` with a constant code and for the category factories (`Error.Validation(message)`,
`Error.NotFound(message)`, …) anywhere except inside a type named `Errors` or whose name ends in `Errors`
(`OrderErrors`), at any nesting depth. Not reported: `Error.Create` with a code that is not a constant (code that
translates another system's errors), and `Error.Validation(details)`, which groups existing errors. Tests, demos and
adapters that create errors on purpose can turn the rule off in `.editorconfig`.

## RU0007

**`WithField` where the field name is not known.** The field is the name of the input in the request, and only the
code that reads the request knows it: the same `Email` may come from `email`, `contact` or `owner.email`.

```csharp
public static Result<Email> Create(string? input) =>
    string.IsNullOrWhiteSpace(input) ? Errors.Required.WithField("email") : new Email(input);    // RU0007

Email.Create(command.Email).MapError(e => e.WithField("email"));    // fine: in the handler that reads the request
```

Reported in a static factory with one parameter of a simple type (string, number, Boolean, date, `Guid`, enum) that
returns a result of its own type (`Email.Create(string)`), and inside an error catalog. A value object with several
inputs (`OrderLine.Create(product, quantity)`, or one parameter object) names its own parts and is not reported.

## RU0008

**A method that returns a `ValueTask` of a result.** The pipeline's delegates and return values are `Task`-based: a
`ValueTask` can be awaited only once, and every step after it returns a `Task` anyway.

```csharp
public ValueTask<Result<User>> FindAsync(Guid id);    // RU0008
public Task<Result<User>> FindAsync(Guid id);         // fine
```

Reported for methods, local functions and properties you declare. Not reported for overrides and interface
implementations, whose signature is given, nor for a `ValueTask` from someone else's API used as a source
(`db.Users.FindAsync([id], ct).ToResultAsync(error)`), which the pipeline accepts.

## RU0009

**`Result<Error>`, an error as a success value.** Both cases of the union are then errors: a `switch` on the cases
cannot tell a success from a failure, and an error produced by a step travels down the chain as a success, so the
steps after it run. C# has no constraint that excludes one type, so `Result<T>` cannot forbid it; the analyzer does.

```csharp
var checkedUser = user.Map(u => Errors.Blocked(u.Id));                          // RU0009: a success whose value is an error
var checkedUser = user.Ensure(u => !u.IsBlocked, u => Errors.Blocked(u.Id));    // fine: a failure when blocked

Result<Error> last;                                                             // RU0009
```

Reported where `Result<Error>` (or a task of one) is written, and where a call produces it from something that was not
one: `Map` / `MapAsync` with a mapper that returns an `Error`, or a generic method closed over `Error`. The steps that
follow on the same chain are not reported again. Usually the step meant `Bind` (a mapper that returns a result),
`Ensure` (a condition) or `MapError` (a different error). `Result<Error[]>` and other types that contain errors are
not reported.

**Code fix:** for `Map` / `MapAsync` with a lambda whose body is an `Error` (also after `await`), the step fails with
that error and keeps the value type: `user.Map(u => Errors.Blocked(u.Id))` becomes
`user.Bind(u => Result<User>.Failure(Errors.Blocked(u.Id)))`. Where only some values should fail, `Ensure` says so
better; the condition is not in the code, so change it by hand then. A `Result<Error>` written out as a type, a generic
method closed over `Error` and a method group get no fix.

## RU0010

**A `Result` written as or read from JSON.** A result is not a JSON contract: it is mapped where it leaves the
application. System.Text.Json in .NET 11 treats `Result<T>` as a union and writes its case, so an endpoint that
returns a result sends a failure as `200 OK` with the error as the body, and the client takes it for a success.

```csharp
app.MapGet("/products/{id}", (Guid id, ProductService s, CancellationToken ct) =>
    s.FindAsync(id, ct));                                                    // RU0010: Task<Result<Product>> as the body

app.MapGet("/products/{id}", (Guid id, ProductService s, CancellationToken ct) =>
    s.FindAsync(id, ct).MapAsync(ProductResponse.From).ToHttpResultAsync());  // fine: 200 or a problem response

JsonSerializer.Serialize(result);                                            // RU0010
JsonSerializer.Serialize(new OrderRejected(order.Id, error));               // fine: an Error is serializable
```

Reported for a Minimal API handler (`MapGet`, `MapPost`, … with a lambda or a method) that returns a result or a task
of one, and for a result passed to, or read by, the JSON methods of `JsonSerializer`, `Results` / `TypedResults`,
`WriteAsJsonAsync` / `ReadFromJsonAsync` and `HttpClient` (`GetFromJsonAsync<Result<T>>`). The library backs it up at
run time: writing or reading a `Result` or `Result<T>` throws `NotSupportedException` with the same advice, for the
cases the analyzer cannot see (a result passed on as `object`).

**Code fix:** for an endpoint lambda with an expression body, in a project that references ResultUnion.AspNetCore, the
body returns its response: `() => s.FindAsync(id, ct)` becomes `() => s.FindAsync(id, ct).ToHttpResultAsync()`
(`.ToHttpResult()` for a result that is not a task), and `using ResultUnion.AspNetCore;` is added when it is missing.
That is 200 OK with the value (204 for a `Result`) or a problem response; for another success response, such as
201 Created, pass it: `ToHttpResultAsync(v => TypedResults.Created(…))`. A block body or a method group can return
in several places, and a result serialized elsewhere needs a type of its own, so those get no fix.
