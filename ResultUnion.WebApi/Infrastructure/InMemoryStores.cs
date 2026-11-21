using System.Collections.Concurrent;
using ResultUnion.Samples.Domain;

namespace ResultUnion.WebApi.Infrastructure;

/// <summary>A thread-safe in-memory stand-in for a database table of users. Task.Delay simulates latency.</summary>
public sealed class InMemoryUserStore
{
    private static readonly TimeSpan Latency = TimeSpan.FromMilliseconds(5);

    private readonly ConcurrentDictionary<string, User> _users = new(StringComparer.Ordinal);

    /// <summary>Like EF Core's <c>FirstOrDefaultAsync</c>: <see langword="null"/> when there is no match.</summary>
    public async Task<User?> FindAsync(Email email, CancellationToken cancellationToken)
    {
        await Task.Delay(Latency, cancellationToken);

        return _users.GetValueOrDefault(email.Value);
    }

    public async Task<Result> EnsureEmailIsFreeAsync(Email email, CancellationToken cancellationToken)
    {
        await Task.Delay(Latency, cancellationToken);

        return _users.ContainsKey(email.Value)
            ? User.Errors.EmailAlreadyRegistered(email)
            : Result.Success();
    }

    /// <summary>Adds the user, or fails when another request registered the same email in the meantime.</summary>
    public async Task<Result<User>> AddAsync(User user, CancellationToken cancellationToken)
    {
        await Task.Delay(Latency, cancellationToken);

        return _users.TryAdd(user.Email.Value, user)
            ? user
            : User.Errors.EmailAlreadyRegistered(user.Email);
    }

    public async Task<IReadOnlyList<User>> ListAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(Latency, cancellationToken);

        return [.. _users.Values.OrderBy(user => user.Email.Value, StringComparer.Ordinal)];
    }

    public void Seed(User user) => _users[user.Email.Value] = user;
}

public sealed class InMemoryOrderStore
{
    private readonly ConcurrentDictionary<Guid, Order> _orders = new();

    public Task AddAsync(Order order, CancellationToken cancellationToken)
    {
        _orders[order.Id] = order;

        return Task.CompletedTask;
    }
}

/// <summary>A per-email quota: at most <see cref="Limit"/> password resets per <see cref="Window"/>.</summary>
public sealed class PasswordResetQuota(TimeProvider time)
{
    public const int Limit = 3;

    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private readonly ConcurrentDictionary<string, Queue<DateTimeOffset>> _requests = new(StringComparer.Ordinal);

    public Result TryConsume(Email email)
    {
        var now = time.GetUtcNow();
        var queue = _requests.GetOrAdd(email.Value, _ => new Queue<DateTimeOffset>());

        lock (queue)
        {
            while (queue.Count > 0 && now - queue.Peek() >= Window)
            {
                queue.Dequeue();
            }

            if (queue.Count >= Limit)
            {
                var retryAfter = (int)Math.Ceiling((Window - (now - queue.Peek())).TotalSeconds);
                return User.Errors.PasswordResetLimit(email, retryAfter);
            }

            queue.Enqueue(now);
            return Result.Success();
        }
    }
}
