namespace ResultUnion.CodeFixes;

/// <summary>The analyzer rules these code fixes handle (see ResultUnion.Analyzers and docs/ANALYZERS.md).</summary>
public static class DiagnosticIds
{
    /// <summary>A result is discarded.</summary>
    public const string DiscardedResult = "RU0001";

    /// <summary>Asynchronous code passed to Result.Try.</summary>
    public const string AsyncInTry = "RU0002";

    /// <summary>Map with a mapper that returns a result.</summary>
    public const string MapReturnsResult = "RU0003";

    /// <summary>A catch of OperationCanceledException that turns cancellation into a result.</summary>
    public const string CancellationAsFailure = "RU0005";

    /// <summary>A Result&lt;Error&gt;: an error as a success value.</summary>
    public const string ErrorAsValue = "RU0009";

    /// <summary>A result written as or read from JSON, such as an endpoint handler that returns one.</summary>
    public const string SerializedResult = "RU0010";
}
