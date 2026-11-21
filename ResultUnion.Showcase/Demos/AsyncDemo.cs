namespace ResultUnion.Showcase.Demos;

/// <summary>Async pipelines: MapAsync, BindAsync, EnsureAsync, TapAsync, TapErrorAsync, MapErrorAsync and MatchAsync.</summary>
internal static class AsyncDemo
{
    public static async Task RunAsync()
    {
        var repository = new UserRepository();
        var service = new AsyncRegistrationService(repository, new WelcomeEmailSender());

        // A full async use case: only one 'await', at the end.
        (string? Email, string? Age)[] requests =
        [
            ("ann@example.com", "34"),     // ok: stored, welcome email sent
            ("ANN@example.com", "40"),     // conflict, found by the async check
            ("bob@example.com", "15"),     // business rule
            ("bob@example.com", "abc"),    // validation, sync step after the first await
        ];

        Console.WriteLine();
        Console.WriteLine("  Async registration pipeline:");
        foreach (var (email, age) in requests)
        {
            Console.WriteLine($"  RegisterAsync(\"{email}\", \"{age}\")");

            var message = await service.RegisterAsync(email, age)
                .MatchAsync(
                    user => $"registered {user.Email} (age {user.Age})",
                    error => $"error {error.Code}: {error.Message}");

            Demo.Show("    -> MatchAsync(...)", message);
        }

        // One async step in the middle of a pipeline: Result<Email> -> Task<Result<User>> -> Task<Result<int>>.
        Console.WriteLine();
        Console.WriteLine("  Single async step:");
        foreach (var email in new[] { "ann@example.com", "zoe@example.com" })
        {
            var age = await Email.Create(email)
                .BindAsync(address => repository.FindByEmailAsync(address))
                .MapAsync(user => user.Age);

            Demo.Show($"FindByEmailAsync(\"{email}\")", age);
        }

        // Result without a value, and MapErrorAsync translating the error code.
        Console.WriteLine();
        Console.WriteLine("  Async deactivation (Task<Result>):");
        Demo.Show("DeactivateAsync(\"ann@example.com\")", await service.DeactivateAsync("ann@example.com"));
        Demo.Show("DeactivateAsync(\"ann@example.com\") again", await service.DeactivateAsync("ann@example.com"));
        Demo.Show("DeactivateAsync(\"zoe@example.com\")", await service.DeactivateAsync("zoe@example.com"));

        // Starting from Task<Result> and continuing into a value: Task<Result> -> Task<Result<User>>.
        Console.WriteLine();
        Console.WriteLine("  Task<Result> into Task<Result<T>>:");
        Demo.Show("RegisterAsAdminAsync(\"guest\", ...)", await service.RegisterAsAdminAsync("guest", "carl@example.com", "52"));
        Console.WriteLine("  RegisterAsAdminAsync(\"admin\", ...)");
        Demo.Show("    -> MapAsync(user => user.Email)", await service.RegisterAsAdminAsync("admin", "carl@example.com", "52").MapAsync(user => user.Email));

        // One generic behavior for handlers with different result types: Result<User> and Result.
        Console.WriteLine();
        Console.WriteLine("  Generic pipeline behavior (IResultFactory<TSelf>):");
        Demo.Show("RequireRole(guest) -> RegisterAsync", await AuthorisationBehavior.RequireRoleAsync(
            "guest", "admin", () => service.RegisterAsync("dora@example.com", "41")));       // Task<Result<User>>
        Demo.Show("RequireRole(guest) -> DeactivateAsync", await AuthorisationBehavior.RequireRoleAsync(
            "guest", "admin", () => service.DeactivateAsync("carl@example.com")));           // Task<Result>
        Console.WriteLine("  RequireRole(admin) -> DeactivateAsync");
        Demo.Show("    -> result", await AuthorisationBehavior.RequireRoleAsync(
            "admin", "admin", () => service.DeactivateAsync("carl@example.com")));

        // Cancellation is not a business failure: it stays an exception and is not turned into a Result.
        Console.WriteLine();
        Console.WriteLine("  Cancellation (CancellationToken passed by closure):");
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        try
        {
            await service.RegisterAsync("dan@example.com", "30", cancellation.Token);
        }
        catch (OperationCanceledException exception)
        {
            Demo.Show("RegisterAsync(..., cancelled token)", $"error {exception.GetType().Name}: {exception.Message}");
        }
    }
}
