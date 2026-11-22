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

    public static readonly DiagnosticDescriptor CancellationAsFailure = new(
        id: "RU0005",
        title: "Cancellation turned into a failure",
        messageFormat: "This catch turns {0} into a result; let cancellation propagate, or filter it with "
            + "'when (!cancellationToken.IsCancellationRequested)' for a timeout",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Cancellation is not a business failure: the caller asked to stop, and a result would make the work "
            + "look finished (a failure) or even successful. A catch of OperationCanceledException or "
            + "TaskCanceledException without a 'when' filter that returns a result swallows the request; the library "
            + "itself never catches cancellation (Result.Try, Result.TryAsync).",
        helpLinkUri: HelpBase + "ru0005");

    public static readonly DiagnosticDescriptor InlineError = new(
        id: "RU0006",
        title: "Error created outside an error catalog",
        messageFormat: "The error '{0}' is created inline; declare it in the Errors catalog of the concept it is about",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Error codes are a contract with clients, who switch on them and translate them. Declared in one "
            + "catalog per domain concept (a nested class named Errors, or a class whose name ends in Errors), each code "
            + "has one definition; created inline in handlers and validators, codes multiply and drift. Groups built with "
            + "Error.Validation(details) are not reported.",
        helpLinkUri: HelpBase + "ru0006");

    public static readonly DiagnosticDescriptor FieldSetTooEarly = new(
        id: "RU0007",
        title: "Field set where the field name is not known",
        messageFormat: "WithField is called in {0}, which cannot know what the caller's input is called; set the field "
            + "where the value is read from the request",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "A value object that is itself one input (Email.Create(string)) and an error catalog do not know "
            + "the name of the field the value came from; the request might call it 'email', 'contact' or "
            + "'owner.email'. The caller sets it: Email.Create(command.Email).MapError(e => e.WithField(\"email\")). A "
            + "value object with several inputs names its own parts and is not reported.",
        helpLinkUri: HelpBase + "ru0007");

    public static readonly DiagnosticDescriptor ValueTaskResult = new(
        id: "RU0008",
        title: "ValueTask of a Result",
        messageFormat: "'{0}' returns {1}; return Task<…> instead, as the rest of the pipeline does",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "The pipeline's delegates and return values are Task-based: a ValueTask can be awaited only once, "
            + "and every step after it returns a Task anyway. A ValueTask from someone else's API (EF Core's FindAsync) is "
            + "fine as a source; methods you write return Task<Result<T>>. Overrides and interface implementations, "
            + "whose signature is given, are not reported.",
        helpLinkUri: HelpBase + "ru0008");

    public static readonly DiagnosticDescriptor ErrorAsValue = new(
        id: "RU0009",
        title: "Result<Error>, an error as a success value",
        messageFormat: "{0} a success whose value is an error; return the error as a failure instead (Bind rather "
            + "than Map, Result<T>.Failure, or 'return error;')",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "In a Result<Error> both cases of the union are errors: a switch on the cases cannot tell a "
            + "success from a failure, and a failure produced by a step reaches the rest of the chain as a success. It "
            + "usually comes from Map with a mapper that returns an error, where Bind or MapError was meant. A type "
            + "parameter cannot exclude Error, so the analyzer reports Result<Error> where it is written and where a call "
            + "produces it.",
        helpLinkUri: HelpBase + "ru0009");

    public static readonly DiagnosticDescriptor SerializedResult = new(
        id: "RU0010",
        title: "A Result written as or read from JSON",
        messageFormat: "{0}; a result is not a JSON contract, so map it at the boundary (ToHttpResult, Match or a "
            + "switch into a type of your own)",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "System.Text.Json in .NET 11 writes a Result<T> as its union case, so an endpoint that returns a "
            + "result sends a failure as 200 OK with the error as the body, and the client takes it for a success. "
            + "Map the result where it leaves the application: ToHttpResult in an endpoint, a DTO for a message. An "
            + "Error on its own is serializable. At run time the library refuses to serialize a result too, with "
            + "NotSupportedException; this rule finds the same mistake while you build.",
        helpLinkUri: HelpBase + "ru0010");
}
