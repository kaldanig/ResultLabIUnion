using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ResultUnion.Analyzers.Tests;

/// <summary>
/// Applies a code fix to a snippet, one diagnostic at a time, until the analyzer reports none of the diagnostics the fix
/// handles. Fails when the fixed code does not compile, so a fix cannot produce broken code unnoticed.
/// </summary>
internal static class CodeFixHarness
{
    public sealed record FixResult(string Code, IReadOnlyList<string> Titles);

    /// <param name="analyzer">The analyzer that reports the fix's diagnostics; <see cref="ResultUsageAnalyzer"/> when null.</param>
    public static async Task<FixResult> FixAsync(string source, CodeFixProvider fix, DiagnosticAnalyzer? analyzer = null)
    {
        var cancellation = TestContext.Current.CancellationToken;
        analyzer ??= new ResultUsageAnalyzer();

        using var workspace = new AdhocWorkspace();
        var document = CreateDocument(workspace, source);

        var titles = new List<string>();
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var diagnostic = (await AnalyzeAsync(document, analyzer, cancellation))
                .Where(d => fix.FixableDiagnosticIds.Contains(d.Id))
                .OrderBy(d => d.Location.SourceSpan.Start)
                .FirstOrDefault();

            if (diagnostic is null)
                break;

            var actions = new List<CodeAction>();
            await fix.RegisterCodeFixesAsync(new CodeFixContext(document, diagnostic, (action, _) => actions.Add(action), cancellation));
            var action = Assert.Single(actions);
            titles.Add(action.Title);

            var operation = Assert.Single((await action.GetOperationsAsync(cancellation)).OfType<ApplyChangesOperation>());
            document = operation.ChangedSolution.GetDocument(document.Id)!;
        }

        var compilation = (await document.Project.GetCompilationAsync(cancellation))!;
        var errors = compilation.GetDiagnostics(cancellation).Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        var code = (await document.GetTextAsync(cancellation)).ToString()[AnalyzerHarness.Usings.Length..];
        Assert.True(errors.Count == 0, "The fixed code does not compile:\n" + string.Join("\n", errors) + "\n\n" + code);

        return new FixResult(code, titles);
    }

    /// <summary>The titles of the fixes offered for each diagnostic the fix handles, without applying any.</summary>
    public static async Task<IReadOnlyList<string>> OfferedAsync(string source, CodeFixProvider fix, DiagnosticAnalyzer analyzer)
    {
        var cancellation = TestContext.Current.CancellationToken;

        using var workspace = new AdhocWorkspace();
        var document = CreateDocument(workspace, source);

        var titles = new List<string>();
        foreach (var diagnostic in (await AnalyzeAsync(document, analyzer, cancellation)).Where(d => fix.FixableDiagnosticIds.Contains(d.Id)))
            await fix.RegisterCodeFixesAsync(new CodeFixContext(document, diagnostic, (action, _) => titles.Add(action.Title), cancellation));
        return titles;
    }

    private static Document CreateDocument(AdhocWorkspace workspace, string source) =>
        workspace.AddProject("Snippet", LanguageNames.CSharp)
            .WithParseOptions(new CSharpParseOptions(LanguageVersion.Preview))
            .WithCompilationOptions(new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable))
            .WithMetadataReferences(AnalyzerHarness.References)
            .AddDocument("Snippet.cs", AnalyzerHarness.Usings + source);

    private static async Task<IReadOnlyList<Diagnostic>> AnalyzeAsync(Document document, DiagnosticAnalyzer analyzer, CancellationToken cancellation)
    {
        var compilation = (await document.Project.GetCompilationAsync(cancellation))!;
        return await compilation.WithAnalyzers([analyzer]).GetAnalyzerDiagnosticsAsync(cancellation);
    }
}
