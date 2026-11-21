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
/// RU0002: replaces <c>Result.Try</c> with <c>Result.TryAsync</c>. Inside an <see langword="async"/> function the call is
/// also awaited (parenthesized when the result is used further, as in <c>(await Result.TryAsync(...)).Match(...)</c>), so
/// the surrounding code keeps its type; elsewhere the caller gets a <c>Task</c> to await.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(TryAsyncCodeFix)), Shared]
public sealed class TryAsyncCodeFix : CodeFixProvider
{
    public const string Title = "Use Result.TryAsync";
    public const string TitleWithAwait = "Use await Result.TryAsync";

    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create(DiagnosticIds.AsyncInTry);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);

        // The diagnostic covers the delegate argument; the call is the closest invocation of a member named Try.
        var invocation = root?.FindNode(context.Span, getInnermostNodeForTie: true)
            .AncestorsAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .FirstOrDefault(call => call.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Try" });

        if (invocation is null)
            return;

        var title = IsInAsyncFunction(invocation) && invocation.Parent is not AwaitExpressionSyntax ? TitleWithAwait : Title;

        context.RegisterCodeFix(
            CodeAction.Create(title, token => UseTryAsync(context.Document, invocation, token), equivalenceKey: Title),
            context.Diagnostics);
    }

    private static async Task<Document> UseTryAsync(Document document, InvocationExpressionSyntax invocation, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);

        var access = (MemberAccessExpressionSyntax)invocation.Expression;
        var name = access.Name.Identifier;
        var renamed = access.Name.WithIdentifier(SyntaxFactory.Identifier(name.LeadingTrivia, "TryAsync", name.TrailingTrivia));
        ExpressionSyntax replacement = invocation.WithExpression(access.WithName(renamed));

        if (IsInAsyncFunction(invocation) && invocation.Parent is not AwaitExpressionSyntax)
        {
            replacement = SyntaxFactory.AwaitExpression(
                SyntaxFactory.Token(SyntaxKind.AwaitKeyword).WithTrailingTrivia(SyntaxFactory.Space),
                replacement.WithoutTrivia());

            // "await" binds looser than member access: Result.Try(...).Match(...) becomes (await ...).Match(...).
            if (invocation.Parent is MemberAccessExpressionSyntax or ConditionalAccessExpressionSyntax or ElementAccessExpressionSyntax)
                replacement = SyntaxFactory.ParenthesizedExpression(replacement);

            // The call's own trivia (indentation before, a line break after) stays outside the new parentheses.
            replacement = replacement
                .WithLeadingTrivia(invocation.GetLeadingTrivia())
                .WithTrailingTrivia(invocation.GetTrailingTrivia());
        }

        return document.WithSyntaxRoot(root!.ReplaceNode(invocation, replacement));
    }

    private static bool IsInAsyncFunction(SyntaxNode node)
    {
        foreach (var ancestor in node.Ancestors())
        {
            switch (ancestor)
            {
                case AnonymousFunctionExpressionSyntax function:
                    return function.AsyncKeyword.IsKind(SyntaxKind.AsyncKeyword);
                case LocalFunctionStatementSyntax local:
                    return local.Modifiers.Any(SyntaxKind.AsyncKeyword);
                case MethodDeclarationSyntax method:
                    return method.Modifiers.Any(SyntaxKind.AsyncKeyword);
                case AccessorDeclarationSyntax or ConstructorDeclarationSyntax or PropertyDeclarationSyntax:
                    return false;
            }
        }

        return false;
    }
}
