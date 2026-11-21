namespace ResultUnion.Samples.Application;

/// <summary>
/// A pipeline behavior in the style of MediatR: one generic wrapper for handlers with any result type.
/// <see cref="IResultFactory{TSelf}"/> lets it return a failure of a type it only knows as <typeparamref name="TResponse"/>.
/// </summary>
/// <remarks>The role comes from an <c>X-Role</c> header to keep the demo self-contained; a real API uses authentication.</remarks>
public static class RoleBehavior
{
    public static async Task<TResponse> RequireRoleAsync<TResponse>(string? role, string requiredRole, Func<Task<TResponse>> handler)
        where TResponse : IResultFactory<TResponse> =>
        string.Equals(role, requiredRole, StringComparison.OrdinalIgnoreCase)
            ? await handler()
            : TResponse.Failure(Error.Forbidden($"Role '{role ?? "(none)"}' cannot do this; '{requiredRole}' is required."));
}
