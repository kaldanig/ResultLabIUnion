using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace ResultUnion.Analyzers;

/// <summary>RU0005: a catch of <c>OperationCanceledException</c> without a filter that returns a result.</summary>
[DiagnosticAnalyzer(Microsoft.CodeAnalysis.LanguageNames.CSharp)]
public sealed class CancellationAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Descriptors.CancellationAsFailure);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterOperationAction(AnalyzeCatch, OperationKind.CatchClause);
    }

    private static void AnalyzeCatch(OperationAnalysisContext context)
    {
        var clause = (ICatchClauseOperation)context.Operation;

        // "catch (TaskCanceledException) when (!ct.IsCancellationRequested)" tells a timeout from a cancellation: fine.
        if (clause.Filter is not null || !IsCancellation(clause.ExceptionType))
            return;

        if (!ReturnsAResult(clause.Handler))
            return;

        var location = clause.Syntax is CatchClauseSyntax { Declaration: { } declaration }
            ? declaration.GetLocation()
            : clause.Syntax.GetLocation();

        context.ReportDiagnostic(Diagnostic.Create(Descriptors.CancellationAsFailure, location, clause.ExceptionType.Name));
    }

    // OperationCanceledException or a type derived from it (TaskCanceledException).
    private static bool IsCancellation(ITypeSymbol? type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (current is { Name: "OperationCanceledException", ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } })
                return true;
        }
        return false;
    }

    // A return in the handler itself (not in a lambda or local function declared there) whose value is a result or an
    // error. The returned value's type is the one after the conversion to the return type, so an error or a fallback
    // value (return -1; in a method returning Result<int>) that becomes a result counts too.
    private static bool ReturnsAResult(IOperation handler) =>
        Descendants(handler).OfType<IReturnOperation>().Any(ret =>
            ret.ReturnedValue is { } value && (ResultTypes.IsResultOrTaskOfResult(value.Type) || IsError(value.Type)));

    private static IEnumerable<IOperation> Descendants(IOperation operation)
    {
        foreach (var child in operation.ChildOperations)
        {
            if (child is IAnonymousFunctionOperation or ILocalFunctionOperation)
                continue;

            yield return child;
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }

    private static bool IsError(ITypeSymbol? type) =>
        type is INamedTypeSymbol { Name: "Error", ContainingNamespace: { Name: "ResultUnion", ContainingNamespace.IsGlobalNamespace: true } };
}
