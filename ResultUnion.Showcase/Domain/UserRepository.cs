namespace ResultUnion.Showcase.Domain;

/// <summary>An in-memory stand-in for a database.</summary>
public sealed class UserRepository
{
    private readonly Dictionary<string, User> _users = new(StringComparer.Ordinal);

    public Result<User> FindByEmail(Email email) =>
        _users.TryGetValue(email.Value, out var user)
            ? user
            : Error.NotFound($"No user with email {email}.");

    /// <summary>A <see cref="Result"/>-returning check, reusable through <c>Ensure(Func&lt;T, Result&gt;)</c>.</summary>
    public Result EnsureEmailIsFree(Email email) =>
        _users.ContainsKey(email.Value)
            ? Error.Conflict($"Email {email} is already registered.")
            : Result.Success();

    public void Add(User user) => _users[user.Email.Value] = user;

    // Async versions: Task.Delay stands in for database latency.

    private static readonly TimeSpan Latency = TimeSpan.FromMilliseconds(20);

    public async Task<Result<User>> FindByEmailAsync(Email email, CancellationToken cancellationToken = default)
    {
        await Task.Delay(Latency, cancellationToken);

        return FindByEmail(email);
    }

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
