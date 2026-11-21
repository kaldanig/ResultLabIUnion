namespace ResultUnion.Showcase.Domain;

/// <summary>A use case written as one railway pipeline: every step runs only if the previous one succeeded.</summary>
public sealed class RegistrationService(UserRepository repository)
{
    public Result<User> Register(string? emailInput, string? ageInput) =>
        Email.Create(emailInput)                                            // Result<Email>
            .Ensure(repository.EnsureEmailIsFree)                           // reuse a Result-returning check, keep the Email
            .Bind(email => User.ParseAge(ageInput)                          // Result<int>
                .Map(age => new User(Guid.NewGuid(), email, age)))          // Result<User>
            .Ensure(user => user.IsAdult, User.Errors.TooYoung)             // domain rule, domain error
            .Tap(repository.Add)                                            // side effect only on success
            .TapError(error => Console.WriteLine($"    [log] registration rejected: {error}"));

    public Result Deactivate(string? emailInput) =>
        Email.Create(emailInput)
            .Bind(email => repository.FindByEmail(email)
                .MapError(error => error.Type == ErrorType.NotFound
                    ? User.Errors.NotFound(email)                           // infrastructure error -> domain error
                    : error))
            .Bind(user => user.Deactivate());                               // Result<User> -> Result
}
