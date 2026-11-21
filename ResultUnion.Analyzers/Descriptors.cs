using Microsoft.CodeAnalysis;

namespace ResultUnion.Analyzers;

internal static class Descriptors
{
    private const string Category = "Usage";
    private const string HelpBase = "https://github.com/kaldanig/ResultLabIUnion/blob/master/docs/ANALYZERS.md#";

    public static readonly DiagnosticDescriptor DiscardedResult = new(
        id: "RU0001",
        title: "A Result is discarded",
        messageFormat: "The {0} returned by '{1}' is discarded, so a failure would go unnoticed",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A failure is an ordinary return value: if the result is not used, the error is lost. Handle it "
            + "(Match, switch, return it, chain it), or discard it explicitly with '_ = ...' when ignoring it is intended.",
        helpLinkUri: HelpBase + "ru0001");

    public static readonly DiagnosticDescriptor AsyncInTry = new(
        id: "RU0002",
        title: "Asynchronous code passed to Result.Try",
        messageFormat: "Result.Try does not await '{0}': exceptions after the first await escape; use Result.TryAsync",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Result.Try runs its delegate synchronously. An async lambda becomes a Task value (or an async void "
            + "Action), so only exceptions thrown before the first await are caught. Result.TryAsync awaits the task.",
        helpLinkUri: HelpBase + "ru0002");

    public static readonly DiagnosticDescriptor MapReturnsResult = new(
        id: "RU0003",
        title: "Map with a mapper that returns a Result",
        messageFormat: "The mapper returns {0}, so '{1}' produces a nested result; use '{2}' instead",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Map is for steps that cannot fail (T to U). A step that returns a Result belongs in Bind, which "
            + "flattens it; Map would produce Result<Result<U>> and hide the inner failure from the rest of the chain.",
        helpLinkUri: HelpBase + "ru0003");

    public static readonly DiagnosticDescriptor UncheckedAccess = new(
        id: "RU0004",
        title: "ResultValue or Error read without checking the result",
        messageFormat: "'{0}' is read without checking that the result is a {1}, so it throws when it is not; "
            + "use switch, Match or {2}, or check {3} first",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "ResultValue throws on a failure and Error throws on a success. Read them only where the state is "
            + "known: inside 'if (result.IsSuccess)', after 'if (result.IsFailure) return ...;', on the matching side of "
            + "?: or &&, or after TryGetResultValue / TryGetError. A switch on the union or Match needs no check at all.",
        helpLinkUri: HelpBase + "ru0004");
}
