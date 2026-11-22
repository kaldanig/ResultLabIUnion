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
using Microsoft.CodeAnalysis.Formatting;

namespace ResultUnion.CodeFixes;

/// <summary>
/// RU0010: an endpoint handler that returns a result becomes one that returns its HTTP response,
/// <c>() =&gt; service.FindAsync(id, ct).ToHttpResultAsync()</c>: a success is 200 OK (or 204 for a <c>Result</c>), a
/// failure a problem response.
/// </summary>
/// <remarks>
/// Offered for a lambda with an expression body, when the project references ResultUnion.AspNetCore; it adds
/// <c>using ResultUnion.AspNetCore;</c> when the file and the project's global usings lack it. A block body or a method
/// group can return in several places, and a result written as JSON elsewhere needs a type of its own: no fix there.
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ToHttpResultCodeFix)), Shared]
public sealed class ToHttpResultCodeFix : CodeFixProvider
{
    private const string Namespace = "ResultUnion.AspNetCore";

    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create(DiagnosticIds.SerializedResult);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);

        // The diagnostic covers the handler argument of MapGet, MapPost, …
        var node = root?.FindNode(context.Span, getInnermostNodeForTie: true);
        var lambda = (node as ArgumentSyntax)?.Expression as LambdaExpressionSyntax ?? node as LambdaExpressionSyntax;
        if (lambda?.ExpressionBody is not { } body)
            return;

        var model = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (model?.Compilation.GetTypeByMetadataName(Namespace + ".ResultHttpExtensions") is null)
            return;   // no ResultUnion.AspNetCore: nothing to call

        // The body's own type: Result<T> in "() => Find()", but also in "async () => await FindAsync()".
        var method = IsTask(model.GetTypeInfo(body, context.CancellationToken).Type) ? "ToHttpResultAsync" : "ToHttpResult";

        context.RegisterCodeFix(
            CodeAction.Create($"Return {method}()", token => AppendAsync(context.Document, body, method, token), equivalenceKey: "ToHttpResult"),
            context.Diagnostics);
    }

    private static async Task<Document> AppendAsync(Document document, ExpressionSyntax body, string method, CancellationToken cancellationToken)
    {
        var root = (CompilationUnitSyntax)(await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false))!;

        // "x ? a : b", "await y" and other expressions bind looser than a member access: parenthesize them.
        ExpressionSyntax receiver = body is InvocationExpressionSyntax or MemberAccessExpressionSyntax or SimpleNameSyntax
            or ElementAccessExpressionSyntax or ParenthesizedExpressionSyntax or ObjectCreationExpressionSyntax
            ? body.WithoutTrivia()
            : SyntaxFactory.ParenthesizedExpression(body.WithoutTrivia());

        var call = SyntaxFactory.InvocationExpression(
                SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, receiver, SyntaxFactory.IdentifierName(method)))
            .WithTriviaFrom(body);

        root = root.ReplaceNode(body, call);

        if (!await IsImportedAsync(document, root, cancellationToken).ConfigureAwait(false))
        {
            root = root.AddUsings(SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(Namespace))
                .WithAdditionalAnnotations(Formatter.Annotation));
        }

        return document.WithSyntaxRoot(root);
    }

    // In this file or as a global using anywhere in the project (ImplicitUsings, <Using Include="…" />).
    private static async Task<bool> IsImportedAsync(Document document, CompilationUnitSyntax root, CancellationToken cancellationToken)
    {
        if (root.Usings.Any(IsNamespace))
            return true;

        foreach (var other in document.Project.Documents)
        {
            if (other.Id == document.Id)
                continue;
            if (await other.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false) is CompilationUnitSyntax unit
                && unit.Usings.Any(u => u.GlobalKeyword.IsKind(SyntaxKind.GlobalKeyword) && IsNamespace(u)))
                return true;
        }

        return false;

        static bool IsNamespace(UsingDirectiveSyntax directive) =>
            directive.Alias is null && directive.StaticKeyword.IsKind(SyntaxKind.None) && directive.Name?.ToString() == Namespace;
    }

    private static bool IsTask(ITypeSymbol? type) =>
        type is INamedTypeSymbol { Name: "Task" or "ValueTask", ContainingNamespace: var ns } && ns.ToDisplayString() == "System.Threading.Tasks";
}
