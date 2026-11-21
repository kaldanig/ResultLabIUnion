namespace ResultUnion.Showcase.Application;

/// <summary>
/// A pipeline behavior in the style of MediatR: one generic wrapper for handlers with any result type.
/// <see cref="IResultFactory{TSelf}"/> is what lets it return a failure of a type it only knows as <typeparamref name="TResponse"/>.
/// </summary>
public static class AuthorisationBehavior
{
    public static async Task<TResponse> RequireRoleAsync<TResponse>(string role, string requiredRole, Func<Task<TResponse>> handler)
        where TResponse : IResultFactory<TResponse>
    {
        if (role != requiredRole)
        {
            return TResponse.Failure(Error.Forbidden($"Role '{role}' is not '{requiredRole}'."));  // short-circuit: the handler does not run
        }

        var response = await handler();

        // IResultFactory<T> is also an IResultState, so the behavior can inspect the outcome (log, commit or roll back, …).
        Console.WriteLine($"    [audit] {requiredRole} action {(response.IsSuccess ? "succeeded" : $"failed: {response.Error.Code}")}");

        return response;
    }
}
