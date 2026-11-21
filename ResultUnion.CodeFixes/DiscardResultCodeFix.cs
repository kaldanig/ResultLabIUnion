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

/// <summary>RU0001: discards the result explicitly, <c>_ = Parse(input);</c>, when ignoring it is intended.</summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(DiscardResultCodeFix)), Shared]
public sealed class DiscardResultCodeFix : CodeFixProvider
{
    public const string Title = "Discard the result explicitly (_ = ...)";

    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create(DiagnosticIds.DiscardedResult);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);

        // The diagnostic covers the statement, or the expression body of a lambda that returns nothing.
        var expression = root?.FindNode(context.Span, getInnermostNodeForTie: true) switch
        {
            ExpressionStatementSyntax statement => statement.Expression,
            ExpressionSyntax body => body,
            _ => null,
        };

        if (expression is null)
            return;

        context.RegisterCodeFix(
            CodeAction.Create(Title, token => DiscardAsync(context.Document, expression, token), equivalenceKey: Title),
            context.Diagnostics);
    }

    private static async Task<Document> DiscardAsync(Document document, ExpressionSyntax expression, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);

        // "_" must carry the contextual kind UnderscoreToken (as the parser gives it), or the compiler binds it as an
        // ordinary name and reports CS0103; SyntaxFactory.IdentifierName("_") does not set it.
        var underscore = SyntaxFactory.Identifier(
            SyntaxTriviaList.Empty, SyntaxKind.UnderscoreToken, "_", "_", SyntaxTriviaList.Empty);

        var discard = SyntaxFactory.AssignmentExpression(
                SyntaxKind.SimpleAssignmentExpression,
                SyntaxFactory.IdentifierName(underscore),
                SyntaxFactory.Token(SyntaxKind.EqualsToken).WithLeadingTrivia(SyntaxFactory.Space).WithTrailingTrivia(SyntaxFactory.Space),
                expression.WithoutLeadingTrivia())
            .WithLeadingTrivia(expression.GetLeadingTrivia());

        return document.WithSyntaxRoot(root!.ReplaceNode(expression, discard));
    }
}
