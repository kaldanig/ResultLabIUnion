using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace ResultUnion.Analyzers;

/// <summary>
/// RU0009: a <c>Result&lt;Error&gt;</c>, a success whose value is an error. C# cannot exclude a type argument with a
/// constraint, so the analyzer reports it where it is written and where a call produces it.
/// </summary>
[DiagnosticAnalyzer(Microsoft.CodeAnalysis.LanguageNames.CSharp)]
public sealed class ErrorAsValueAnalyzer : DiagnosticAnalyzer
{
    private static readonly SymbolDisplayFormat TypeFormat = SymbolDisplayFormat.MinimallyQualifiedFormat;

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Descriptors.ErrorAsValue);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzeTypeName, SyntaxKind.GenericName);
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
    }

    // Result<Error> written out: a declaration, a cast, Result<Error>.Success(...), new / default(Result<Error>).
    private static void AnalyzeTypeName(SyntaxNodeAnalysisContext context)
    {
        var name = (GenericNameSyntax)context.Node;
        if (name.Identifier.ValueText != "Result" || name.TypeArgumentList.Arguments.Count != 1)
            return;

        if (context.SemanticModel.GetSymbolInfo(name, context.CancellationToken).Symbol is not INamedTypeSymbol type
            || !IsResultOfError(type))
            return;

        context.ReportDiagnostic(Diagnostic.Create(Descriptors.ErrorAsValue, name.GetLocation(), "The type Result<Error> is"));
    }

    // A call that produces a Result<Error> (or a task of one) from something that was not one: Map(x => error),
    // MapAsync, a generic method closed over Error. The steps that follow on the same chain are not reported again.
    private static void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        if (!IsResultOfErrorOrTask(invocation.Type))
            return;

        var method = invocation.TargetMethod;
        if (IsResultOfError(method.ContainingType))
            return;   // Result<Error>.Success(...): the type name is reported

        var inputs = invocation.Arguments.Select(a => a.Value);
        if (invocation.Instance is not null)
            inputs = inputs.Prepend(invocation.Instance);
        if (inputs.Any(input => IsResultOfErrorOrTask(Unwrap(input).Type)))
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            Descriptors.ErrorAsValue,
            Location(invocation),
            $"'{method.Name}' returns {invocation.Type!.ToDisplayString(TypeFormat)},"));
    }

    // The method name of a call, so a long chain is not underlined as a whole.
    private static Location Location(IInvocationOperation invocation) => invocation.Syntax switch
    {
        InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax member } => member.Name.GetLocation(),
        InvocationExpressionSyntax { Expression: MemberBindingExpressionSyntax binding } => binding.Name.GetLocation(),
        InvocationExpressionSyntax { Expression: SimpleNameSyntax simple } => simple.GetLocation(),
        _ => invocation.Syntax.GetLocation(),
    };

    private static IOperation Unwrap(IOperation operation)
    {
        while (operation is IConversionOperation { IsImplicit: true } conversion)
            operation = conversion.Operand;
        return operation;
    }

    private static bool IsResultOfErrorOrTask(ITypeSymbol? type) =>
        IsResultOfError(type) || (ResultTypes.IsAwaitable(type, out var inner) && IsResultOfError(inner));

    private static bool IsResultOfError(ITypeSymbol? type) =>
        ResultTypes.IsResult(type)
        && type is INamedTypeSymbol { TypeArguments.Length: 1 } result
        && result.TypeArguments[0] is INamedTypeSymbol { Name: "Error", ContainingNamespace: { Name: "ResultUnion", ContainingNamespace.IsGlobalNamespace: true } };
}
