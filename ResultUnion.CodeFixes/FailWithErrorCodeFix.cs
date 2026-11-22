using System.Collections.Immutable;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ResultUnion.CodeFixes;

/// <summary>
/// RU0009: <c>Map</c> with a mapper that returns an error makes a <c>Result&lt;Error&gt;</c>. The fix makes the step
/// fail with that error and keep the value type: <c>user.Map(u =&gt; Errors.Blocked(u.Id))</c> becomes
/// <c>user.Bind(u =&gt; Result&lt;User&gt;.Failure(Errors.Blocked(u.Id)))</c>, and <c>MapAsync</c> becomes
/// <c>BindAsync</c>.
/// </summary>
/// <remarks>
/// Offered for a lambda with an expression body whose value is an <c>Error</c> (also after <c>await</c>). Where only
/// some values should fail, <c>Ensure</c> fits better, but the condition is not in the code, so that is left to the
/// reader. A <c>Result&lt;Error&gt;</c> written out as a type gets no fix.
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(FailWithErrorCodeFix)), Shared]
public sealed class FailWithErrorCodeFix : CodeFixProvider
{
    public const string Title = "Fail with the error (Bind)";

    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create(DiagnosticIds.ErrorAsValue);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);

        // The diagnostic covers the method name of the call that produces the Result<Error>.
        if (root?.FindNode(context.Span, getInnermostNodeForTie: true) is not SimpleNameSyntax name
            || name.Identifier.ValueText is not ("Map" or "MapAsync")
            || name.Parent is not MemberAccessExpressionSyntax { Parent: InvocationExpressionSyntax invocation } member
            || invocation.ArgumentList.Arguments.Count != 1
            || invocation.ArgumentList.Arguments[0].Expression is not LambdaExpressionSyntax { ExpressionBody: { } body })
            return;

        var model = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (model is null || !IsError(model.GetTypeInfo(body, context.CancellationToken).Type))
            return;

        // The Result<T> the call starts from (directly or in a task): the failure keeps its T.
        if (Unwrap(model.GetTypeInfo(member.Expression, context.CancellationToken).Type) is not INamedTypeSymbol { TypeArguments.Length: 1 } source)
            return;

        var resultType = source.ToMinimalDisplayString(model, body.SpanStart);

        context.RegisterCodeFix(
            CodeAction.Create(Title, token => FixAsync(context.Document, name, body, resultType, token), equivalenceKey: Title),
            context.Diagnostics);
    }

    private static async Task<Document> FixAsync(Document document, SimpleNameSyntax name, ExpressionSyntax body, string resultType, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);

        var failure = SyntaxFactory.ParseExpression($"{resultType}.Failure({body.WithoutTrivia().ToFullString()})")
            .WithTriviaFrom(body);
        var bind = SyntaxFactory.IdentifierName(SyntaxFactory.Identifier(
            name.GetLeadingTrivia(), name.Identifier.ValueText == "Map" ? "Bind" : "BindAsync", name.GetTrailingTrivia()));   // type arguments go

        var updated = root!.ReplaceNodes([name, body], (original, _) => original == name ? bind : failure);
        return document.WithSyntaxRoot(updated);
    }

    private static ITypeSymbol? Unwrap(ITypeSymbol? type) =>
        type is INamedTypeSymbol { Name: "Task" or "ValueTask", TypeArguments.Length: 1 } task
        && task.ContainingNamespace.ToDisplayString() == "System.Threading.Tasks"
            ? task.TypeArguments[0]
            : type;

    private static bool IsError(ITypeSymbol? type) =>
        type is INamedTypeSymbol { Name: "Error", ContainingNamespace: { Name: "ResultUnion", ContainingNamespace.IsGlobalNamespace: true } };
}
