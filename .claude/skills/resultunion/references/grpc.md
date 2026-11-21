# gRPC services and clients with ResultUnion.Grpc

`using ResultUnion.Grpc;` A gRPC method returns the reply message, and gRPC reports failures through the call's status,
so the service ends the pipeline by returning the reply or throwing an `RpcException` with a rich status. Errors are
not modelled in the `.proto` messages.

## Service

```csharp
public sealed class UsersGrpcService(UserService users) : Users.UsersBase
{
    private const string Domain = "users.example.com";     // ErrorInfo.domain: who defines the codes

    public override Task<UserReply> GetUser(UserRequest request, ServerCallContext context) =>
        users.FindAsync(request.Email, context.CancellationToken)
            .MapAsync(ToReply)
            .GetValueOrThrowRpcAsync(Domain);                // reply, or RpcException

    public override Task<Empty> DeactivateUser(UserRequest request, ServerCallContext context) =>
        users.DeactivateAsync(request.Email, context.CancellationToken)
            .GetValueOrThrowRpcAsync(() => new Empty(), Domain);   // Result without a value

    private static UserReply ToReply(User user) => new() { Id = user.Id.ToString(), Email = user.Email.Value };
}
```

Map the request to the same command the HTTP endpoint uses; the handler does not change. Read metadata (e.g. a role)
from `context.RequestHeaders.GetValue("x-role")`.

What the client receives: status code from `ErrorType` (Validation → InvalidArgument, Unauthorized → Unauthenticated,
Forbidden → PermissionDenied, NotFound → NotFound, Conflict/BusinessRule → FailedPrecondition, Concurrency → Aborted,
TooManyRequests → ResourceExhausted, Unavailable → Unavailable, Unexpected → Internal), an `ErrorInfo` (reason
`USER_TOO_YOUNG`; original code, type and arguments in metadata), a `BadRequest` with one field violation per invalid
input, and a `RetryInfo` for rate limits with a numeric `retryAfterSeconds` argument.

## Client

```csharp
Result<UserReply> user = await Result.TryAsync(
    () => client.GetUserAsync(new UserRequest { Email = email }, cancellationToken: ct).ResponseAsync,
    (RpcException ex) => ex.ToError());                     // only RpcException is translated

if (user.TryGetError(out var error) && error.IsTransient) { /* retry with backoff */ }
```

`ToError()` restores type, code, message, arguments and field violations from a ResultUnion service. Argument values
arrive as strings, and individual field violations carry no arguments. A plain status from any other server becomes an
error with the code `grpc.<status>` (e.g. `grpc.deadline_exceeded`).

Use a `oneof` result message only for per-item errors inside a stream, where a status would end the stream.
