using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ResultUnion.Analyzers.Tests;

/// <summary>Compiles a snippet against the real ResultUnion assembly and runs the analyzer on it.</summary>
internal static class AnalyzerHarness
{
    private const string Usings = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using ResultUnion;

        """;

    private static readonly int UsingLines = Usings.Count(c => c == '\n');

    private static readonly ImmutableArray<MetadataReference> References =
    [
        .. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(path => Path.GetFileName(path).StartsWith("System.", StringComparison.Ordinal)
                || Path.GetFileName(path) is "mscorlib.dll" or "netstandard.dll")
            .Select(path => MetadataReference.CreateFromFile(path)),
        MetadataReference.CreateFromFile(typeof(Result).Assembly.Location),
    ];

    /// <summary>
    /// The analyzer's diagnostics as (id, line), with line 1 being the first line of <paramref name="source"/>.
    /// Fails when the snippet itself does not compile, so a test cannot pass by accident.
    /// </summary>
    public static async Task<IReadOnlyList<(string Id, int Line)>> AnalyzeAsync(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(
            Usings + source,
            new CSharpParseOptions(LanguageVersion.Preview),
            cancellationToken: TestContext.Current.CancellationToken);

        var compilation = CSharpCompilation.Create(
            "Snippet",
            [tree],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var errors = compilation.GetDiagnostics(TestContext.Current.CancellationToken)
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        Assert.True(errors.Count == 0, "The snippet does not compile:\n" + string.Join("\n", errors));

        var diagnostics = await compilation
            .WithAnalyzers([new ResultUsageAnalyzer()])
            .GetAnalyzerDiagnosticsAsync(TestContext.Current.CancellationToken);

        return diagnostics
            .Select(d => (d.Id, d.Location.GetLineSpan().StartLinePosition.Line + 1 - UsingLines))
            .OrderBy(d => d.Item2)
            .ToList();
    }
}
