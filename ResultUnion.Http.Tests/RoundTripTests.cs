using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using ResultUnion.AspNetCore;

namespace ResultUnion.Http.Tests;

/// <summary>
/// What ResultUnion.AspNetCore sends, ResultUnion.Http reads back: a real ASP.NET Core server in memory, real HTTP
/// responses, and the error the client gets compared with the one the server returned.
/// </summary>
public sealed class RoundTripTests : IAsyncLifetime
{
    public sealed record User(string Email, int Age);

    private static readonly Error NotFound =
        Error.Create("user.not_found", "No user ann@example.com.", ErrorType.NotFound).WithArgument("email", "ann@example.com");
    private static readonly Error Conflict = Error.Create("user.email_taken", "Taken.", ErrorType.Conflict);
    private static readonly Error Concurrency = Error.Create("order.changed", "Changed by someone else.", ErrorType.Concurrency);
    private static readonly Error TooYoung =
        Error.Create("user.too_young", "Must be 18.", ErrorType.BusinessRule).WithArgument("minimumAge", 18).WithArgument("ratio", 0.5);
    private static readonly Error RateLimited =
        Error.Create("reset.rate_limited", "Too many resets.", ErrorType.TooManyRequests).WithArgument("retryAfterSeconds", 42);
    private static readonly Error Unavailable = Error.Create("payment.unavailable", "Provider down.", ErrorType.Unavailable);
    private static readonly Error OneField =
        Error.Create("email.required", "Email is required.", ErrorType.Validation).WithField("email");
    private static readonly Error Group = Error.Validation(
    [
        Error.Create("email.required", "Email is required.", ErrorType.Validation).WithField("email"),
        Error.Create("password.too_short", "At least 8.", ErrorType.Validation).WithField("password").WithArgument("minimumLength", 8),
        Error.Create("order_line.quantity_not_positive", "Quantity must be positive.", ErrorType.Validation).WithField("lines[1].quantity"),
    ]);
    private static readonly Error Unexpected =
        Error.Create("db.crashed", "Connection string Server=prod;Password=secret", ErrorType.Unexpected).WithArgument("host", "prod");

    private static readonly Dictionary<string, Error> Errors = new()
    {
        ["not-found"] = NotFound, ["conflict"] = Conflict, ["concurrency"] = Concurrency, ["too-young"] = TooYoung,
        ["rate-limited"] = RateLimited, ["unavailable"] = Unavailable, ["one-field"] = OneField, ["group"] = Group,
        ["unexpected"] = Unexpected,
    };

    private WebApplication _app = null!;
    private HttpClient _http = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddProblemDetails();
        _app = builder.Build();

        _app.MapGet("/users/ann", () => Result<User>.Success(new User("ann@example.com", 34)).ToHttpResult());
        _app.MapDelete("/users/ann", () => Result.Success().ToHttpResult());
        _app.MapGet("/errors/{name}", (string name) => Result<User>.Failure(Errors[name]).ToHttpResult());
        _app.MapDelete("/errors/{name}", (string name) => Result.Failure(Errors[name]).ToHttpResult());

        await _app.StartAsync(Ct);
        _http = _app.GetTestClient();
    }

    public async ValueTask DisposeAsync()
    {
        _http.Dispose();
        await _app.DisposeAsync();
    }

    public static TheoryData<string> Names => [.. Errors.Keys.Where(k => k != "unexpected")];

    [Fact]
    public async Task A_success_is_read_with_its_value()
    {
        var user = await _http.GetAsync("/users/ann", Ct).ReadResultAsync<User>(Ct);
        var deleted = await _http.DeleteAsync("/users/ann", Ct).ReadResultAsync(Ct);

        Assert.True(user.TryGetResultValue(out var value));
        Assert.Equal(new User("ann@example.com", 34), value);
        Assert.True(deleted.IsSuccess);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public async Task The_error_comes_back_as_the_server_sent_it(string name)
    {
        var withValue = await _http.GetAsync($"/errors/{name}", Ct).ReadResultAsync<User>(Ct);
        var withoutValue = await _http.DeleteAsync($"/errors/{name}", Ct).ReadResultAsync(Ct);

        Assert.True(withValue.TryGetError(out var error));
        Assert.Equal(Errors[name], error);
        Assert.True(withoutValue.TryGetError(out var same));
        Assert.Equal(Errors[name], same);
    }

    [Fact]
    public async Task A_concurrency_conflict_stays_transient()
    {
        var result = await _http.GetAsync("/errors/concurrency", Ct).ReadResultAsync<User>(Ct);

        Assert.True(result.TryGetError(out var error));
        Assert.Equal(ErrorType.Concurrency, error.Type);
        Assert.True(error.IsTransient);
    }

    [Fact]
    public async Task An_unexpected_error_arrives_without_the_servers_message_and_arguments()
    {
        var result = await _http.GetAsync("/errors/unexpected", Ct).ReadResultAsync<User>(Ct);

        Assert.True(result.TryGetError(out var error));
        Assert.Equal("db.crashed", error.Code);
        Assert.Equal(ErrorType.Unexpected, error.Type);
        Assert.Equal(ProblemDetailsMapping.UnexpectedDetail, error.Message);
        Assert.Empty(error.Arguments);
    }
}
