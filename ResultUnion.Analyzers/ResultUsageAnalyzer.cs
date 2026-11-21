using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace ResultUnion.Analyzers;

/// <summary>
/// RU0001 a discarded result, RU0002 asynchronous code passed to <c>Result.Try</c>, RU0003 <c>Map</c> with a mapper
/// that returns a result.
/// </summary>
[DiagnosticAnalyzer(Microsoft.CodeAnalysis.LanguageNames.CSharp)]
public sealed class ResultUsageAnalyzer : DiagnosticAnalyzer
{
    private static readonly SymbolDisplayFormat TypeFormat = SymbolDisplayFormat.MinimallyQualifiedFormat;

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Descriptors.DiscardedResult, Descriptors.AsyncInTry, Descriptors.MapReturnsResult);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterOperationAction(AnalyzeExpressionStatement, OperationKind.ExpressionStatement);
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
    }

    // ---- RU0001 ---------------------------------------------------------------------------------------------------

    private static void AnalyzeExpressionStatement(OperationAnalysisContext context)
    {
        var statement = (IExpressionStatementOperation)context.Operation;

        // "await SaveAsync();" discards the Result inside the task; "SaveAsync();" discards the task itself.
        var expression = statement.Operation is IAwaitOperation awaited ? awaited.Operation : statement.Operation;
        if (expression is not IInvocationOperation invocation)
            return;

        var discardedType = statement.Operation.Type;
        if (!ResultTypes.IsResultOrTaskOfResult(discardedType))
            return;

        if (IsTapOnAnExistingValue(invocation))
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            Descriptors.DiscardedResult,
            statement.Syntax.GetLocation(),
            discardedType!.ToDisplayString(TypeFormat),
            invocation.TargetMethod.Name));
    }

    // "result.TapError(log);" returns the same result the variable still holds: nothing is lost.
    private static bool IsTapOnAnExistingValue(IInvocationOperation invocation)
    {
        var method = invocation.TargetMethod;
        if (!method.Name.StartsWith("Tap", System.StringComparison.Ordinal) || !ResultTypes.IsLibraryMember(method))
            return false;

        var receiver = invocation.Instance ?? (invocation.Arguments.Length > 0 && method.IsExtensionMethod
            ? invocation.Arguments[0].Value
            : null);

        while (receiver is IConversionOperation { IsImplicit: true } conversion)
            receiver = conversion.Operand;

        return receiver is ILocalReferenceOperation or IParameterReferenceOperation or IFieldReferenceOperation
            or IPropertyReferenceOperation;
    }

    // ---- RU0002 and RU0003 ----------------------------------------------------------------------------------------

    private static void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;

        switch (method.Name)
        {
            case "Try" when method.IsStatic && method.ContainingType is { Name: "Result" } && ResultTypes.IsLibraryMember(method):
                AnalyzeTry(context, invocation);
                break;
            case "Map" or "MapAsync" when ResultTypes.IsLibraryMember(method):
                AnalyzeMap(context, invocation);
                break;
        }
    }

    private static void AnalyzeTry(OperationAnalysisContext context, IInvocationOperation invocation)
    {
        var action = invocation.Arguments.FirstOrDefault(a => a.Parameter?.Ordinal == 0)?.Value;
        if (action is not IDelegateCreationOperation creation)
            return;

        var isAsync = creation.Target switch
        {
            IAnonymousFunctionOperation lambda => lambda.Symbol.IsAsync || ReturnsAwaitable(lambda.Symbol),
            IMethodReferenceOperation reference => reference.Method.IsAsync || ReturnsAwaitable(reference.Method),
            _ => false,
        };

        if (!isAsync)
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            Descriptors.AsyncInTry,
            action.Syntax.GetLocation(),
            Shorten(action.Syntax.ToString())));
    }

    private static void AnalyzeMap(OperationAnalysisContext context, IInvocationOperation invocation)
    {
        var method = invocation.TargetMethod;

        // Map returns Result<TResult>, MapAsync Task<Result<TResult>>: TResult is what the mapper produces.
        var returned = method.ReturnType;
        if (ResultTypes.IsAwaitable(returned, out var inner))
            returned = inner;

        if (returned is not INamedTypeSymbol { TypeArguments.Length: 1 } outer || !ResultTypes.IsResult(outer))
            return;

        var produced = outer.TypeArguments[0];
        if (!ResultTypes.IsResult(produced))
            return;

        var replacement = method.Name == "MapAsync" ? "BindAsync" : "Bind";

        context.ReportDiagnostic(Diagnostic.Create(
            Descriptors.MapReturnsResult,
            NameLocation(invocation),
            produced.ToDisplayString(TypeFormat),
            method.Name,
            replacement));
    }

    // ---- helpers ------------------------------------------------------------------------------------------------

    private static bool ReturnsAwaitable(IMethodSymbol method) => ResultTypes.IsAwaitable(method.ReturnType, out _);

    private static Location NameLocation(IInvocationOperation invocation) =>
        invocation.Syntax is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax access }
            ? access.Name.GetLocation()
            : invocation.Syntax.GetLocation();

    private static string Shorten(string text)
    {
        var singleLine = string.Join(" ", text.Split(new[] { '\r', '\n' }, System.StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Trim()));
        return singleLine.Length <= 60 ? singleLine : singleLine.Substring(0, 57) + "...";
    }
}
