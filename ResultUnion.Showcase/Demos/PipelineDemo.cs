namespace ResultUnion.Showcase.Demos;

/// <summary>Railway-oriented composition: Map, Bind, Ensure, Tap, TapError, MapError and Match.</summary>
internal static class PipelineDemo
{
    public static void Run()
    {
        // The individual operators on a small example.
        var parsed = User.ParseAge("30");
        Demo.Show("ParseAge(\"30\")", parsed);
        Demo.Show("  .Map(age => age + 1)", parsed.Map(age => age + 1));
        Demo.Show("  .Ensure(age => age >= 40, ...)", parsed.Ensure(age => age >= 40, Error.BusinessRule("Too young.")));
        Demo.Show("  .Bind(age => age > 0 ? age * 2 : error)", parsed.Bind(age => age > 0 ? Result.Success(age * 2) : Error.Validation("Must be positive.")));
        Demo.Show("ParseAge(\"abc\").Map(age => age + 1)", User.ParseAge("abc").Map(age => age + 1));
        Demo.Show("  .MapError(e => Unexpected(...))", User.ParseAge("abc").MapError(e => Error.Unexpected($"Wrapped: {e.Message}")));

        // A full use case: every step runs only if the previous one succeeded.
        var repository = new UserRepository();
        var service = new RegistrationService(repository);

        (string? Email, string? Age)[] requests =
        [
            ("ann@example.com", "34"),     // ok
            ("ANN@example.com", "40"),     // conflict: email already registered (emails are normalised)
            ("bob@example.com", "15"),     // business rule: too young
            ("bob@example.com", "abc"),    // validation: age is not a number
            ("", "20"),                    // validation: email is required
        ];

        Console.WriteLine();
        Console.WriteLine("  Registration pipeline:");
        foreach (var (email, age) in requests)
        {
            Console.WriteLine($"  Register(\"{email}\", \"{age}\")");

            var result = service.Register(email, age);              // TapError logs here, on failures only

            // Match turns both tracks into one value at the end of the pipeline.
            var message = result.Match(
                user => $"registered {user.Email} (age {user.Age})",
                error => $"error {error.Code}: {error.Message}");

            Demo.Show("    -> Match(...)", message);
        }

        // A command that returns Result (no value), with MapError translating the error code.
        Console.WriteLine();
        Console.WriteLine("  Deactivation (Result without a value):");
        Demo.Show("Deactivate(\"ann@example.com\")", service.Deactivate("ann@example.com"));
        Demo.Show("Deactivate(\"ann@example.com\") again", service.Deactivate("ann@example.com"));
        Demo.Show("Deactivate(\"zoe@example.com\")", service.Deactivate("zoe@example.com"));

        // Void Match for side effects at the edge of the application.
        Console.WriteLine();
        service.Register("carl@example.com", "52").Match(
            user => Console.WriteLine($"  Match(Action): welcome email sent to {user.Email}"),
            error => Console.WriteLine($"  Match(Action): nothing sent ({error.Code})"));
    }
}
