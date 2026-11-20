namespace ResultUnion.Showcase.Domain;

/// <summary>A use case written as one railway pipeline: every step runs only if the previous one succeeded.</summary>
public sealed class RegistrationService(UserRepository repository)
{
    public const int MinimumAge = 18;

    public Result<User> Register(string? emailInput, string? ageInput) =>
        Email.Create(emailInput)                                            // Result<Email>
            .Ensure(repository.EnsureEmailIsFree)                           // reuse a Result-returning check, keep the Email
            .Bind(email => User.ParseAge(ageInput)                          // Result<int>
                .Map(age => new User(Guid.NewGuid(), email, age)))          // Result<User>
            .Ensure(
                user => user.Age >= MinimumAge,
                user => Error.BusinessRule($"Users must be at least {MinimumAge}; {user.Email} is {user.Age}."))
            .Tap(repository.Add)                                            // side effect only on success
            .TapError(error => Console.WriteLine($"    [log] registration rejected: {error}"));

    public Result Deactivate(string? emailInput) =>
        Email.Create(emailInput)
            .Bind(repository.FindByEmail)
            .MapError(error => error.Type == ErrorType.NotFound
                ? Error.Create("user.not_found", error.Message, ErrorType.NotFound)   // translate to a domain-specific code
                : error)
            .Bind(user => user.Deactivate());                               // Result<User> -> Result
}
