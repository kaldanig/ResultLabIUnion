# ResultUnion.Http

HTTP client integration for [ResultUnion](https://www.nuget.org/packages/ResultUnion): reads an `HttpResponseMessage`
as a `Result`, and an RFC 9457 problem details body back into an `Error`. It is the client side of
`ResultUnion.AspNetCore`, and works with any HTTP API.

```csharp
using ResultUnion.Http;

// The value on a success status, the error from the problem details body otherwise.
Result<UserDto> user = await http.GetAsync($"/users/{email}", ct).ReadResultAsync<UserDto>(ct);

// No value: a success on 2xx.
Result deleted = await http.DeleteAsync($"/users/{email}", ct).ReadResultAsync(ct);

if (user.TryGetError(out var error) && error.Code == "user.not_found") { /* … */ }
```

From a ResultUnion server the error comes back as it was sent: code, type (the `category` member), message, arguments
and one field error per input. From any other server the type follows the status code (404 → NotFound, 409 →
Conflict, 429 → TooManyRequests, 503 → Unavailable, …), the code is `http.<status>` (`http.not_found`), ASP.NET Core's
`errors` member becomes field errors, and a `Retry-After` header becomes the `retryAfterSeconds` argument.

Network failures and timeouts stay exceptions; translate the ones you expect with
`Result.TryAsync(..., (HttpRequestException ex) => ...)`. No ASP.NET Core dependency: it runs in Blazor WebAssembly,
MAUI, workers and other services.

- Documentation: <https://kaldanig.github.io/ResultLabIUnion/>
- Source: <https://github.com/kaldanig/ResultLabIUnion>
