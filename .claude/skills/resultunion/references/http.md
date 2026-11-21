# HTTP endpoints with ResultUnion.AspNetCore

`using ResultUnion.AspNetCore;` The endpoint maps the request to a command, runs the handler, maps the value to a DTO,
and turns the result into a typed response. Failures become RFC 9457 problem responses automatically.

```csharp
var users = app.MapGroup("/users").WithTags("Users");

users.MapPost("/", (RegisterUserRequest request, UserService service, CancellationToken ct) =>
        service.RegisterAsync(new RegisterUser(request.Email, request.Password, request.Age), ct)
            .MapAsync(UserResponse.From)                                              // never serialize the Result
            .ToHttpResultAsync(user => TypedResults.Created($"/users/{user.Id}", user)))
    .ProducesErrors(ErrorType.Validation, ErrorType.Conflict, ErrorType.BusinessRule);

users.MapGet("/{email}", (string email, UserService service, CancellationToken ct) =>
        service.FindAsync(email, ct).MapAsync(UserResponse.From).ToHttpResultAsync())  // 200 OK
    .ProducesErrors(ErrorType.Validation, ErrorType.NotFound);

users.MapDelete("/{email}", (string email, UserService service, CancellationToken ct) =>
        service.DeactivateAsync(email, ct).ToHttpResultAsync())                         // Result → 204 No Content
    .ProducesErrors(ErrorType.NotFound, ErrorType.Conflict);
```

| Call | Returns |
|---|---|
| `Result<T>.ToHttpResult()` | `Results<Ok<T>, ProblemHttpResult>` |
| `Result<T>.ToHttpResult(v => TypedResults.Created(…))` | `Results<Created<T>, ProblemHttpResult>` |
| `Result.ToHttpResult()` | `Results<NoContent, ProblemHttpResult>` |
| `Result.ToHttpResult(() => TypedResults.Accepted(…))` | `Results<Accepted, ProblemHttpResult>` |
| `Task<…>.ToHttpResultAsync(…)` | the same, after `await` |

Why typed: OpenAPI documents the success response from the return type, so no `.Produces<T>()` is needed. A problem's
status is only known at run time, so list the possible categories with `.ProducesErrors(...)` (works on groups too).
Only when the success can be one of several responses, return `IResult` from the lambda (the untyped overload) and add
`.Produces<T>()` yourself.

Status codes: Validation 400, Unauthorized 401, Forbidden 403, NotFound 404, Conflict/Concurrency 409, BusinessRule 422,
TooManyRequests 429, Unavailable 503, Unexpected 500 (message and arguments hidden).

The problem body carries `code`, `arguments`, and for field errors `errors` (messages per field) and `details`
(`code`, `field`, `message`, `arguments` per invalid input; nested groups are flattened). A client should switch on
`code`, show `errors[field]` next to inputs, and use `arguments` to fill a translated template. Translation, if any,
belongs to the host (e.g. an endpoint filter reading `Accept-Language`), not to the domain.

`error.ToProblemDetails()` gives the `ProblemDetails` object for controllers or exception handlers;
`error.ToHttpResult()` gives a `ProblemHttpResult`.
