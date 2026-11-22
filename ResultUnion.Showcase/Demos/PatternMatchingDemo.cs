using ResultUnion.AspNetCore;

namespace ResultUnion.Showcase.Demos;

/// <summary>Result&lt;T&gt; is a C# 15 union: its cases can be matched with switch and is, without boxing.</summary>
internal static class PatternMatchingDemo
{
    public static void Run()
    {
#pragma warning disable RU0004 // constant addresses that are valid
        var ann = new User(Guid.NewGuid(), Email.Create("ann@example.com").ResultValue, 34);
        var bob = new User(Guid.NewGuid(), Email.Create("bob@example.com").ResultValue, 71);
#pragma warning restore RU0004

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

        // Each category tells the caller what to do: the HTTP status (ResultUnion.AspNetCore), and whether a retry may succeed.
        foreach (var type in Enum.GetValues<ErrorType>())
        {
            Demo.Show($"{type}", $"HTTP {type.ToHttpStatusCode()}{(type.IsTransient ? ", transient: retry may succeed" : "")}");
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
}
