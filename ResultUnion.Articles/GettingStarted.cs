using ResultUnion;
using ResultUnion.AspNetCore;

namespace ResultUnion.Articles;

public sealed record RegisterUser(string? Email, string? Password, string? Age);

public interface IUserRepository
{
    Task<Result> EnsureEmailIsFreeAsync(Email email, CancellationToken ct);
    Task AddAsync(User user, CancellationToken ct);
}

public sealed class UserService(IUserRepository users)
{
    // section 3 (verbatim)
    public static Result<User> Validate(RegisterUser command) =>
        Result.CombineAll(
                Email.Create(command.Email).MapError(e => e.WithField("email")),
                Password.Create(command.Password).MapError(e => e.WithField("password")),
                User.ParseAge(command.Age).MapError(e => e.WithField("age")))
            .Bind((email, password, age) => User.Create(email, password, age));

    // section 4 (verbatim)
    public Task<Result<User>> RegisterAsync(RegisterUser command, CancellationToken ct) =>
        Validate(command)
            .Ensure(user => user.Age >= 18, User.Errors.TooYoung)
            .EnsureAsync(user => users.EnsureEmailIsFreeAsync(user.Email, ct))
            .TapAsync(user => users.AddAsync(user, ct));
}

public static class GettingStartedSnippets
{
    // section 2 (verbatim)
    public static string Read(string? input)
    {
        string message = Email.Create(input) switch
        {
            Email email => $"Hello, {email.Value}",
            Error error => $"{error.Code}: {error.Message}",
        };
        return message;
    }

    // section 5 (verbatim)
    public static void Map(WebApplication app)
    {
        app.MapPost("/users", (RegisterUser command, UserService service, CancellationToken ct) =>
                service.RegisterAsync(command, ct)
                    .MapAsync(UserResponse.From)
                    .ToHttpResultAsync(user => TypedResults.Created($"/users/{user.Id}", user)))
            .ProducesErrors(ErrorType.Validation, ErrorType.Conflict, ErrorType.BusinessRule);
    }
}

public sealed class InMemoryUsers(string? existing = null) : IUserRepository
{
    private readonly List<User> _users = [];

    public Task<Result> EnsureEmailIsFreeAsync(Email email, CancellationToken ct) =>
        Task.FromResult(email.Value == existing || _users.Any(u => u.Email == email)
            ? Result.Failure(User.Errors.EmailTaken(email))
            : Result.Success());

    public Task AddAsync(User user, CancellationToken ct)
    {
        _users.Add(user);
        return Task.CompletedTask;
    }
}
