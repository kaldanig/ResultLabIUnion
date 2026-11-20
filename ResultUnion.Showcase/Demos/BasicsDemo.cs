using ResultUnion.Showcase.Domain;

namespace ResultUnion.Showcase.Demos;

/// <summary>Creating results and reading their state.</summary>
internal static class BasicsDemo
{
    public static void Run()
    {
        // Factories.
        var success = Result<int>.Success(42);
        var failure = Result<int>.Failure(Error.NotFound("Order 7 was not found."));
        Demo.Show("Result<int>.Success(42)", success);
        Demo.Show("Result<int>.Failure(NotFound)", failure);

        // Type inference through the non-generic Result.
        Demo.Show("Result.Success(\"hi\")  // Result<string>", Result.Success("hi"));

        // Union conversion: a case converts implicitly to the union, so methods can 'return value;' or 'return error;'.
        Result<int> converted = 7;
        Result<int> convertedError = Error.Validation("Bad input.");
        Demo.Show("Result<int> r = 7;", converted);
        Demo.Show("Result<int> r = Error.Validation(...);", convertedError);

        // A value object factory that returns Result<T> instead of throwing.
        Demo.Show("Email.Create(\"Ann@Example.com\")", Email.Create("Ann@Example.com"));
        Demo.Show("Email.Create(\"not-an-email\")", Email.Create("not-an-email"));

        // State.
        Demo.Show("success.IsSuccess", success.IsSuccess);
        Demo.Show("failure.IsFailure", failure.IsFailure);
        Demo.Show("failure.Error.Type", failure.Error.Type);

        // Non-generic Result for operations without a value.
        Demo.Show("Result.Success()", Result.Success());
        Demo.Show("Result r = Error.Forbidden(...);", (Result)Error.Forbidden("Admins only."));

        // A default result is never a "half" state: it is a failure with a dedicated error.
        Demo.Show("default(Result<int>)", default(Result<int>));

        // Equality is by value.
        // ReSharper disable once EqualExpressionComparison
        Demo.Show("Success(1) == Success(1)", Result<int>.Success(1) == Result<int>.Success(1));
        // ReSharper disable once EqualExpressionComparison
        Demo.Show("NotFound(\"x\") == NotFound(\"x\")", Error.NotFound("x") == Error.NotFound("x"));
    }
}
