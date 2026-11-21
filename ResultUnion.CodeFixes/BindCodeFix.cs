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
/// RU0003: replaces <c>Map</c> with <c>Bind</c> and <c>MapAsync</c> with <c>BindAsync</c> when the mapper returns a
/// result. Explicit type arguments are dropped: <c>Map&lt;Result&lt;U&gt;&gt;</c> would be wrong for <c>Bind</c>, whose
/// type argument is inferred from the mapper.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(BindCodeFix)), Shared]
public sealed class BindCodeFix : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create(DiagnosticIds.MapReturnsResult);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);

        // The diagnostic covers the method name of the call.
        if (root?.FindNode(context.Span, getInnermostNodeForTie: true) is not SimpleNameSyntax name)
            return;

        var replacement = name.Identifier.ValueText switch
        {
            "Map" => "Bind",
            "MapAsync" => "BindAsync",
            _ => null,
        };

        if (replacement is null)
            return;

        var title = $"Use {replacement}";
        context.RegisterCodeFix(
            CodeAction.Create(title, token => RenameAsync(context.Document, name, replacement, token), equivalenceKey: "UseBind"),
            context.Diagnostics);
    }

    private static async Task<Document> RenameAsync(Document document, SimpleNameSyntax name, string replacement, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var identifier = SyntaxFactory.IdentifierName(
            SyntaxFactory.Identifier(name.Identifier.LeadingTrivia, replacement, name.GetTrailingTrivia()));

        return document.WithSyntaxRoot(root!.ReplaceNode(name, identifier));
    }
}
