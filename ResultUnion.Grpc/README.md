# ResultUnion.Grpc

gRPC integration for [ResultUnion](https://www.nuget.org/packages/ResultUnion): sends an `Error` as gRPC's rich error
model (`google.rpc.Status` with `ErrorInfo`, `BadRequest` and `RetryInfo`) and reads it back on the client.

```csharp
using ResultUnion.Grpc;

// Service: return the reply, or throw an RpcException with a rich status.
public override Task<UserReply> GetUser(UserRequest request, ServerCallContext context) =>
    users.FindAsync(request.Email, context.CancellationToken)
        .MapAsync(UserReply.From)
        .GetValueOrThrowRpcAsync();

// Client: turn the RpcException back into an Error.
Result<UserReply> user = await Result.TryAsync(
    () => client.GetUserAsync(new UserRequest { Email = email }, cancellationToken: ct).ResponseAsync,
    (RpcException ex) => ex.ToError());
```

`ErrorType` maps to a status code (Validation → InvalidArgument, NotFound → NotFound, Conflict and BusinessRule →
FailedPrecondition, Concurrency → Aborted, TooManyRequests → ResourceExhausted, Unavailable → Unavailable,
Unexpected → Internal). The code, type and arguments travel in `ErrorInfo`, field errors in `BadRequest`, and a
rate limit's delay in `RetryInfo`. Works on servers and clients; no ASP.NET Core dependency.

Documentation: https://github.com/kaldanig/ResultLabIUnion
