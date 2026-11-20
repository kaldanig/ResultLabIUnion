using ResultUnion.Showcase.Domain;

namespace ResultUnion.Showcase.Demos;

/// <summary>Result&lt;T&gt; is a C# 15 union: its cases can be matched with switch and is, without boxing.</summary>
internal static class PatternMatchingDemo
{
    public static void Run()
    {
        var ann = new User(Guid.NewGuid(), Email.Create("ann@example.com").ResultValue, 34);
        var bob = new User(Guid.NewGuid(), Email.Create("bob@example.com").ResultValue, 71);

        Result<User>[] results =
        [
            ann,
            bob,
            Error.NotFound("No user with id 9."),
            Error.Forbidden("Admins only."),
        ];

        foreach (var result in results)
        {
            var label = result.Match(user => $"user {user.Email}, age {user.Age}", error => error.Code);
            Demo.Show($"Describe({label})", Describe(result));
        }

        // 'is' with a case type, and with a property pattern on the case.
        Result<int> answer = 42;
        if (answer is int number)
        {
            Demo.Show("answer is int number", number);
        }

        Result<int> missing = Error.NotFound("Nothing here.");
        Demo.Show("missing is int", missing is int);
        Demo.Show("missing is Error { Type: NotFound }", missing is Error { Type: ErrorType.NotFound });

        // A common use: mapping an error category to an HTTP status code.
        foreach (var type in Enum.GetValues<ErrorType>())
        {
            Demo.Show($"ToStatusCode({type})", ToStatusCode(Error.Create("demo", "Demo.", type)));
        }
    }

    // The compiler checks that both cases (User and Error) are handled.
    private static string Describe(Result<User> result) => result switch
    {
        User { Age: >= 65 } user => $"senior user {user.Email}",
        User user => $"user {user.Email}, age {user.Age}",
        Error { Type: ErrorType.NotFound } error => $"error: not found ({error.Message})",
        Error error => $"error: {error.Code}",
    };

    private static int ToStatusCode(Error error) => error.Type switch
    {
        ErrorType.Validation => 400,
        ErrorType.Unauthorized => 401,
        ErrorType.Forbidden => 403,
        ErrorType.NotFound => 404,
        ErrorType.Conflict or ErrorType.Concurrency => 409,
        ErrorType.BusinessRule => 422,
        ErrorType.Unavailable => 503,
        _ => 500,
    };
}
