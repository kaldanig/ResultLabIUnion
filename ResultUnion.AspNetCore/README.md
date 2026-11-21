# ResultUnion.AspNetCore

ASP.NET Core integration for [ResultUnion](https://www.nuget.org/packages/ResultUnion): turns a `Result` into a typed
Minimal API response, and an `Error` into an RFC 9457 problem response.

```csharp
using ResultUnion.AspNetCore;

app.MapPost("/users", (RegisterUserRequest request, UserService service, CancellationToken ct) =>
        service.RegisterAsync(request.ToCommand(), ct)
            .MapAsync(UserResponse.From)
            .ToHttpResultAsync(user => TypedResults.Created($"/users/{user.Id}", user)))
    .ProducesErrors(ErrorType.Validation, ErrorType.Conflict);
```

| Call | Returns |
|---|---|
| `Result<T>.ToHttpResult()` | `Results<Ok<T>, ProblemHttpResult>` |
| `Result<T>.ToHttpResult(v => TypedResults.Created(…))` | `Results<Created<T>, ProblemHttpResult>` |
| `Result.ToHttpResult()` | `Results<NoContent, ProblemHttpResult>` |

The typed return documents the success response in OpenAPI; `.ProducesErrors(...)` documents the problem responses.
Each `ErrorType` maps to a status code (Validation 400, NotFound 404, Conflict 409, BusinessRule 422,
TooManyRequests 429, Unavailable 503, Unexpected 500), and the problem body carries `code`, `arguments`, and per-field
`errors` and `details`.

Documentation: https://github.com/kaldanig/ResultLabIUnion
