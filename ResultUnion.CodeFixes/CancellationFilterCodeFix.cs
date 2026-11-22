using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ResultUnion.CodeFixes;

/// <summary>
/// RU0005: a catch of <c>OperationCanceledException</c> that returns a result gets the filter
/// <c>when (!ct.IsCancellationRequested)</c>, so a cancellation requested through <c>ct</c> propagates and only a
/// timeout (HttpClient reports its own as <c>TaskCanceledException</c>) is still turned into a result.
/// </summary>
/// <remarks>
/// Offered once for each <c>CancellationToken</c> parameter or local in scope at the catch. Without one there is no
/// filter to write, and whether to remove the catch or rethrow depends on what the code is for: no fix then.
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(CancellationFilterCodeFix)), Shared]
public sealed class CancellationFilterCodeFix : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create(DiagnosticIds.CancellationAsFailure);

    public override FixAllProvider? GetFixAllProvider() => null;   // the token to use depends on each catch

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);

        // The diagnostic covers the catch declaration, "(OperationCanceledException)".
        if (root?.FindNode(context.Span, getInnermostNodeForTie: true)?.FirstAncestorOrSelf<CatchClauseSyntax>() is not { Filter: null } clause)
            return;

        var model = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (model is null)
            return;

        foreach (var token in TokensInScope(model, clause))
        {
            context.RegisterCodeFix(
                CodeAction.Create(
                    $"Let cancellation of '{token}' propagate (when (!{token}.IsCancellationRequested))",
                    cancellationToken => AddFilterAsync(context.Document, clause, token, cancellationToken),
                    equivalenceKey: "CancellationFilter:" + token),
                context.Diagnostics);
        }
    }

    // Parameters and locals of type CancellationToken visible at the catch, nearest first.
    private static IEnumerable<string> TokensInScope(SemanticModel model, CatchClauseSyntax clause) =>
        model.LookupSymbols(clause.SpanStart)
            .Where(symbol => symbol is IParameterSymbol or ILocalSymbol)
            .Where(symbol => ((symbol as IParameterSymbol)?.Type ?? (symbol as ILocalSymbol)?.Type) is INamedTypeSymbol
            {
                Name: "CancellationToken",
                ContainingNamespace: { Name: "Threading", ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } },
            })
            .Where(symbol => symbol.Locations.FirstOrDefault() is not { IsInSource: true } location || location.SourceSpan.Start < clause.SpanStart)
            .Select(symbol => symbol.Name)
            .Distinct();

    private static async Task<Document> AddFilterAsync(Document document, CatchClauseSyntax clause, string token, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);

        // "catch (X) {" becomes "catch (X) when (!ct.IsCancellationRequested) {": the trivia that followed the declaration
        // (a space, or a line break before the brace) moves behind the filter.
        var declaration = clause.Declaration!;
        var filter = SyntaxFactory.CatchFilterClause(
            SyntaxFactory.Token(SyntaxKind.WhenKeyword).WithTrailingTrivia(SyntaxFactory.Space),
            SyntaxFactory.Token(SyntaxKind.OpenParenToken),
            SyntaxFactory.ParseExpression($"!{token}.IsCancellationRequested"),
            SyntaxFactory.Token(SyntaxKind.CloseParenToken).WithTrailingTrivia(declaration.GetTrailingTrivia()));

        var fixedClause = clause
            .WithDeclaration(declaration.WithTrailingTrivia(SyntaxFactory.Space))
            .WithFilter(filter);

        return document.WithSyntaxRoot(root!.ReplaceNode(clause, fixedClause));
    }
}
