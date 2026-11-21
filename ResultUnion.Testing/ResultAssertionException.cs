namespace ResultUnion.Testing;

/// <summary>Thrown by the ResultUnion assertions when a result or an error is not what the test expected.</summary>
/// <remarks>
/// Every test framework reports an unexpected exception as a failed test, so the assertions need no framework
/// dependency. The message describes what was expected and the whole actual result, details included.
/// </remarks>
public sealed class ResultAssertionException : Exception
{
    /// <summary>Creates the exception with the message to show in the test report.</summary>
    /// <param name="message">What was expected, and what the result or error was instead.</param>
    public ResultAssertionException(string message)
        : base(message)
    {
    }
}
