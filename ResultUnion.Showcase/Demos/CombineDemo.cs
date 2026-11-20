using ResultUnion.Showcase.Domain;

namespace ResultUnion.Showcase.Demos;

/// <summary>Checking several independent results at once: Combine stops at the first failure, CombineAll reports them all.</summary>
internal static class CombineDemo
{
    public static void Run()
    {
        // Span overload: Result values only, no allocations.
        Demo.Show("Combine(Success, Success)", Result.Combine(Result.Success(), Result.Success()));
        Demo.Show("Combine(Success, Forbidden, Conflict)", Result.Combine(
            Result.Success(),
            Error.Forbidden("Admins only."),
            Error.Conflict("Already exists.")));

        // IEnumerable<IResultState> overload: mixes Result<T> of different types (each one is boxed).
        Demo.Show("Validate(\"dan@example.com\", \"28\")", ValidateFirst("dan@example.com", "28"));
        Demo.Show("Validate(\"dan@\", \"abc\")  // first wins", ValidateFirst("dan@", "abc"));

        // CombineAll: every invalid field, each one named with WithField.
        Console.WriteLine();
        Console.WriteLine("  CombineAll, one entry per field:");
        foreach (var (email, age, password) in new[] { ("dan@example.com", "28", "s3cret!pass"), ("dan@", "abc", "123"), ("", "28", "s3cret!pass") })
        {
            var result = ValidateAll(email, age, password);
            Demo.Show($"ValidateAll(\"{email}\", \"{age}\", \"{password}\")", result.IsSuccess ? "Success" : $"Failure({result.Error.Code})");

            if (result.TryGetError(out var error))
            {
                // A single failure comes back unchanged; several are grouped in Details.
                var perField = error.Details.Count > 0 ? error.Details : [error];

                foreach (var detail in perField)
                {
                    Demo.Show($"    {detail.Field}", $"error {detail.Message}");
                }
            }
        }
    }

    private static Result ValidateFirst(string email, string age) =>
        Result.Combine([Email.Create(email), User.ParseAge(age)]);

    private static Result ValidateAll(string email, string age, string password) =>
        Result.CombineAll(
        [
            Email.Create(email).MapError(error => error.WithField("email")),
            User.ParseAge(age).MapError(error => error.WithField("age")),
            password.Length >= 8
                ? Result.Success()
                : Error.Validation("Password must be at least 8 characters.").WithField("password"),
        ]);
}
