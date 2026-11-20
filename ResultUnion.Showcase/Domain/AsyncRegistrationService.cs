namespace ResultUnion.Showcase.Domain;

/// <summary>
/// The async version of <see cref="RegistrationService"/>: synchronous and asynchronous steps mixed in one pipeline.
/// After the first asynchronous step the chain continues on <c>Task&lt;Result&lt;T&gt;&gt;</c>, and only one <c>await</c> is needed, at the end.
/// </summary>
public sealed class AsyncRegistrationService(UserRepository repository, WelcomeEmailSender emailSender)
{
    public Task<Result<User>> RegisterAsync(string? emailInput, string? ageInput, CancellationToken cancellationToken = default) =>
        Email.Create(emailInput)                                                        // Result<Email>             (sync)
            .EnsureAsync(email => repository.EnsureEmailIsFreeAsync(email, cancellationToken))  // Task<Result<Email>>  (async check)
            .BindAsync(email => User.ParseAge(ageInput)                                 // sync step after await
                .Map(age => new User(Guid.NewGuid(), email, age)))                      // Task<Result<User>>
            .EnsureAsync(
                user => user.Age >= RegistrationService.MinimumAge,
                user => Error.BusinessRule($"Users must be at least {RegistrationService.MinimumAge}; {user.Email} is {user.Age}."))
            .TapAsync(user => repository.AddAsync(user, cancellationToken))             // async side effect, awaited
            .TapAsync(user => emailSender.SendAsync(user.Email, cancellationToken))
            .TapErrorAsync(error => Console.WriteLine($"    [log] registration rejected: {error}"));

    public Task<Result> DeactivateAsync(string? emailInput, CancellationToken cancellationToken = default) =>
        Email.Create(emailInput)
            .BindAsync(email => repository.FindByEmailAsync(email, cancellationToken))  // Result<Email> -> Task<Result<User>>
            .MapErrorAsync(error => error.Type == ErrorType.NotFound
                ? Error.Create("user.not_found", error.Message, ErrorType.NotFound)
                : error)
            .BindAsync(user => user.Deactivate());                                      // Task<Result<User>> -> Task<Result>

    /// <summary>Starts from a <c>Task&lt;Result&gt;</c> (no value) and continues into a value-producing step.</summary>
    public Task<Result<User>> RegisterAsAdminAsync(string role, string? emailInput, string? ageInput, CancellationToken cancellationToken = default) =>
        AuthoriseAsync(role, cancellationToken)                                         // Task<Result>
            .BindAsync(() => RegisterAsync(emailInput, ageInput, cancellationToken));   // Task<Result> -> Task<Result<User>>

    private static async Task<Result> AuthoriseAsync(string role, CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(5), cancellationToken);

        return role == "admin"
            ? Result.Success()
            : Error.Forbidden($"Role '{role}' cannot register users.");
    }
}
