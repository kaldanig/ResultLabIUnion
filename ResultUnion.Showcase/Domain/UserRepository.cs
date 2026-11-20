namespace ResultUnion.Showcase.Domain;

/// <summary>An in-memory stand-in for a database.</summary>
public sealed class UserRepository
{
    private readonly Dictionary<string, User> _users = new(StringComparer.Ordinal);

    // A lookup that reports "not found" as null, turned into a Result.
    public Result<User> FindByEmail(Email email) =>
        Result.FromNullable(_users.GetValueOrDefault(email.Value), Error.NotFound($"No user with email {email}."));

    /// <summary>A <see cref="Result"/>-returning check, reusable through <c>Ensure(Func&lt;T, Result&gt;)</c>.</summary>
    public Result EnsureEmailIsFree(Email email) =>
        _users.ContainsKey(email.Value)
            ? Error.Conflict($"Email {email} is already registered.")
            : Result.Success();

    public void Add(User user) => _users[user.Email.Value] = user;

    // Async versions: Task.Delay stands in for database latency.

    private static readonly TimeSpan Latency = TimeSpan.FromMilliseconds(20);

    /// <summary>Like EF Core's <c>FirstOrDefaultAsync</c>: <see langword="null"/> when there is no match.</summary>
    public async Task<User?> FirstOrDefaultAsync(Email email, CancellationToken cancellationToken = default)
    {
        await Task.Delay(Latency, cancellationToken);

        return _users.GetValueOrDefault(email.Value);
    }

    public Task<Result<User>> FindByEmailAsync(Email email, CancellationToken cancellationToken = default) =>
        FirstOrDefaultAsync(email, cancellationToken)
            .ToResultAsync(Error.NotFound($"No user with email {email}."));

    public async Task<Result> EnsureEmailIsFreeAsync(Email email, CancellationToken cancellationToken = default)
    {
        await Task.Delay(Latency, cancellationToken);

        return EnsureEmailIsFree(email);
    }

    public async Task AddAsync(User user, CancellationToken cancellationToken = default)
    {
        await Task.Delay(Latency, cancellationToken);

        Add(user);
    }
}
