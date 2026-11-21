using ResultUnion.Samples.Domain;
using Microsoft.Extensions.Logging;
using ResultUnion.Samples.Application.Infrastructure;

namespace ResultUnion.Samples.Application;

/// <summary>User use cases, each one a single pipeline. Hosts (Web API, gRPC) only map their contract to a command and the result to their protocol.</summary>
public sealed class UserService(InMemoryUserStore users, PasswordResetQuota quota, ILogger<UserService> logger)
{
    /// <summary>Validates every field at once, applies the domain rule, then stores the user.</summary>
    public Task<Result<User>> RegisterAsync(RegisterUser command, CancellationToken cancellationToken) =>
        Result.CombineAll(
                Email.Create(command.Email).MapError(error => error.WithField("email")),
                Password.Create(command.Password).MapError(error => error.WithField("password")),
                User.ParseAge(command.Age).MapError(error => error.WithField("age")))
            .Map((email, _, age) => new User(Guid.NewGuid(), email, age))           // the demo stores no password
            .Ensure(user => user.IsAdult, User.Errors.TooYoung)
            .EnsureAsync(user => users.EnsureEmailIsFreeAsync(user.Email, cancellationToken))
            .BindAsync(user => users.AddAsync(user, cancellationToken))             // also catches a concurrent duplicate
            .TapAsync(user => logger.LogInformation("Registered {Email}", user.Email.Value))
            .TapErrorAsync(error => logger.LogInformation("Registration rejected: {Code}", error.Code))
            .RecordAsync("users.register");                                         // resultunion.errors, by code

    public Task<Result<User>> FindAsync(string? email, CancellationToken cancellationToken) =>
        Email.Create(email)
            .MapError(error => error.WithField("email"))
            .BindAsync(address => users.FindAsync(address, cancellationToken).ToResultAsync(User.Errors.NotFound(address)));

    /// <summary>A command without a value: 204 on success.</summary>
    public Task<Result> DeactivateAsync(string? email, CancellationToken cancellationToken) =>
        FindAsync(email, cancellationToken)
            .BindAsync(user => user.Deactivate());

    /// <summary>Rate limited per email: the fourth request within a minute is <c>429</c> with <c>retryAfterSeconds</c>.</summary>
    public Task<Result> RequestPasswordResetAsync(string? email, CancellationToken cancellationToken) =>
        FindAsync(email, cancellationToken)
            .EnsureAsync(user => quota.TryConsume(user.Email))
            .TapAsync(user => logger.LogInformation("Password reset link sent to {Email}", user.Email.Value))
            .BindAsync(_ => Result.Success());

    public async Task<Result<IReadOnlyList<User>>> ListAsync(CancellationToken cancellationToken) =>
        Result.Success(await users.ListAsync(cancellationToken));
}
