using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using ResultUnion.Grpc;
using ResultUnion.GrpcDemo;
using ResultUnion.Samples.Application;
using User = ResultUnion.Samples.Domain.User;

namespace ResultUnion.GrpcService.Services;

/// <summary>
/// The same use cases as the Web API, behind a gRPC contract. Each method maps the request to a command, runs the
/// use case, and ends with GetValueOrThrowRpcAsync: the reply on success, an RpcException with a rich status on failure.
/// </summary>
public sealed class UsersGrpcService(UserService users) : Users.UsersBase
{
    /// <summary>The error domain in ErrorInfo: who defines the codes.</summary>
    internal const string Domain = "demo.resultunion";

    public override Task<UserReply> RegisterUser(RegisterUserRequest request, ServerCallContext context) =>
        users.RegisterAsync(new RegisterUser(request.Email, request.Password, request.Age), context.CancellationToken)
            .MapAsync(ToReply)
            .GetValueOrThrowRpcAsync(Domain);

    public override Task<UserReply> GetUser(UserRequest request, ServerCallContext context) =>
        users.FindAsync(request.Email, context.CancellationToken)
            .MapAsync(ToReply)
            .GetValueOrThrowRpcAsync(Domain);

    public override Task<Empty> DeactivateUser(UserRequest request, ServerCallContext context) =>
        users.DeactivateAsync(request.Email, context.CancellationToken)
            .GetValueOrThrowRpcAsync(() => new Empty(), Domain);

    public override Task<Empty> RequestPasswordReset(UserRequest request, ServerCallContext context) =>
        users.RequestPasswordResetAsync(request.Email, context.CancellationToken)
            .GetValueOrThrowRpcAsync(() => new Empty(), Domain);

    // The generic IResultFactory behavior, with the role from call metadata instead of an HTTP header.
    public override Task<UserList> ListUsers(Empty request, ServerCallContext context) =>
        RoleBehavior.RequireRoleAsync(context.RequestHeaders.GetValue("x-role"), "admin", () => users.ListAsync(context.CancellationToken))
            .MapAsync(list => new UserList { Users = { list.Select(ToReply) } })
            .GetValueOrThrowRpcAsync(Domain);

    private static UserReply ToReply(User user) => new()
    {
        Id = user.Id.ToString(),
        Email = user.Email.Value,
        Age = user.Age,
        IsActive = user.IsActive,
    };
}
