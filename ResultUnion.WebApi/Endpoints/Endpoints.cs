using Microsoft.AspNetCore.Mvc;
using ResultUnion.AspNetCore;
using ResultUnion.WebApi.Application;

namespace ResultUnion.WebApi.Endpoints;

// Endpoints do one thing: turn the use case's result into HTTP. Typed ToHttpResult documents the success response,
// ProducesErrors documents the problems, so Scalar shows every response without hand-written metadata.

public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder routes)
    {
        var users = routes.MapGroup("/users").WithTags("Users");

        users.MapPost("/", (RegisterUserRequest request, UserService service, CancellationToken ct) =>
                service.RegisterAsync(request, ct)
                    .MapAsync(UserResponse.From)
                    .ToHttpResultAsync(user => TypedResults.Created($"/users/{user.Email}", user)))
            .WithName("RegisterUser")
            .WithSummary("Register a user")
            .WithDescription("Try an invalid email, a short password, age \"abc\" or 15, or ann@example.com (already registered).")
            .ProducesErrors(ErrorType.Validation, ErrorType.Conflict, ErrorType.BusinessRule);

        users.MapGet("/{email}", (string email, UserService service, CancellationToken ct) =>
                service.FindAsync(email, ct)
                    .MapAsync(UserResponse.From)
                    .ToHttpResultAsync())
            .WithName("GetUser")
            .WithSummary("Find a user by email")
            .ProducesErrors(ErrorType.Validation, ErrorType.NotFound);

        users.MapDelete("/{email}", (string email, UserService service, CancellationToken ct) =>
                service.DeactivateAsync(email, ct)
                    .ToHttpResultAsync())
            .WithName("DeactivateUser")
            .WithSummary("Deactivate a user")
            .WithDescription("A second call returns 409 user.already_inactive.")
            .ProducesErrors(ErrorType.Validation, ErrorType.NotFound, ErrorType.Conflict);

        users.MapPost("/{email}/password-reset", (string email, UserService service, CancellationToken ct) =>
                service.RequestPasswordResetAsync(email, ct)
                    .ToHttpResultAsync(() => TypedResults.Accepted((string?)null)))
            .WithName("RequestPasswordReset")
            .WithSummary("Request a password reset link")
            .WithDescription("Rate limited: the fourth request within a minute returns 429 with arguments.retryAfterSeconds.")
            .ProducesErrors(ErrorType.Validation, ErrorType.NotFound, ErrorType.TooManyRequests);

        return routes;
    }
}

public static class OrderEndpoints
{
    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/orders", (PlaceOrderRequest request, OrderService service, CancellationToken ct) =>
                service.PlaceAsync(request, ct)
                    .MapAsync(OrderResponse.From)
                    .ToHttpResultAsync(order => TypedResults.Created($"/orders/{order.Id}", order)))
            .WithTags("Orders")
            .WithName("PlaceOrder")
            .WithSummary("Place an order")
            .WithDescription("Every invalid field is reported, each line with its index: lines[1].quantity.")
            .ProducesErrors(ErrorType.Validation, ErrorType.NotFound);

        return routes;
    }
}

public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/admin/users", ([FromHeader(Name = "X-Role")] string? role, UserService service, CancellationToken ct) =>
                RoleBehavior.RequireRoleAsync(role, "admin", () => service.ListAsync(ct))
                    .MapAsync(list => list.Select(UserResponse.From).ToArray())
                    .ToHttpResultAsync())
            .WithTags("Admin")
            .WithName("ListUsers")
            .WithSummary("List all users (requires X-Role: admin)")
            .ProducesErrors(ErrorType.Forbidden);

        return routes;
    }
}
