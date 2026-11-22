using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace ResultUnion.Analyzers;

/// <summary>
/// The library's conventions, as suggestions (Info): RU0006 an error created outside a catalog, RU0007 a field set by
/// code that cannot know the field's name, RU0008 a method that returns a <c>ValueTask</c> of a result.
/// </summary>
[DiagnosticAnalyzer(Microsoft.CodeAnalysis.LanguageNames.CSharp)]
public sealed class ConventionsAnalyzer : DiagnosticAnalyzer
{
    private static readonly SymbolDisplayFormat TypeFormat = SymbolDisplayFormat.MinimallyQualifiedFormat;

    // The Error factories that create a single error with a code and a message. Error.Validation(IEnumerable<Error>)
    // groups existing errors and is not one of them.
    private static readonly ImmutableHashSet<string> Factories = ImmutableHashSet.Create(
        "Create", "Validation", "Unauthorized", "Forbidden", "NotFound", "Conflict", "Concurrency", "BusinessRule",
        "TooManyRequests", "Unavailable", "Unexpected");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Descriptors.InlineError, Descriptors.FieldSetTooEarly, Descriptors.ValueTaskResult);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
        context.RegisterSymbolAction(AnalyzeMethod, SymbolKind.Method);
        context.RegisterOperationAction(AnalyzeLocalFunction, OperationKind.LocalFunction);
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;
        if (!IsErrorType(method.ContainingType))
            return;

        if (method.IsStatic && Factories.Contains(method.Name) && method.Parameters.All(p => p.Type.SpecialType == SpecialType.System_String || p.Type.TypeKind == TypeKind.Enum))
            AnalyzeFactory(context, invocation);
        else if (!method.IsStatic && method.Name == "WithField")
            AnalyzeWithField(context, invocation);
    }

    // ---- RU0006 ---------------------------------------------------------------------------------------------------

    private static void AnalyzeFactory(OperationAnalysisContext context, IInvocationOperation invocation)
    {
        if (IsInCatalog(context.ContainingSymbol))
            return;

        string code;
        if (invocation.TargetMethod.Name == "Create")
        {
            // Error.Create(code, …) with a code that is not a constant translates another system's error (an HTTP
            // problem, a gRPC status): nothing is invented here.
            if (invocation.Arguments.FirstOrDefault(a => a.Parameter?.Ordinal == 0)?.Value.ConstantValue is not { HasValue: true, Value: string text })
                return;
            code = text;
        }
        else
        {
            code = $"Error.{invocation.TargetMethod.Name}";
        }

        context.ReportDiagnostic(Diagnostic.Create(Descriptors.InlineError, invocation.Syntax.GetLocation(), code));
    }

    // ---- RU0007 ---------------------------------------------------------------------------------------------------

    private static void AnalyzeWithField(OperationAnalysisContext context, IInvocationOperation invocation)
    {
        var member = context.ContainingSymbol;
        if (IsInCatalog(member))
        {
            Report(context, invocation, "an error catalog");
            return;
        }

        // A factory of a single-input value object: static, one parameter of a simple type (Email.Create(string)),
        // returning Result<ItsOwnType> or a task of it. A parameter object (OrderLine.Create(OrderLineInput)) carries
        // several inputs, which the value object names itself.
        var factory = Enclosing(member);
        if (factory is { IsStatic: true, Parameters.Length: 1 } && IsSimple(factory.Parameters[0].Type)
            && ReturnsResultOf(factory, factory.ContainingType))
            Report(context, invocation, $"{factory.ContainingType.Name}.{factory.Name}");

        static void Report(OperationAnalysisContext context, IInvocationOperation invocation, string where) =>
            context.ReportDiagnostic(Diagnostic.Create(Descriptors.FieldSetTooEarly, invocation.Syntax.GetLocation(), where));
    }

    // string, a number, a Boolean, a date, a Guid, an enum, or a nullable one of those: one input.
    private static bool IsSimple(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
            type = nullable.TypeArguments[0];

        return type.SpecialType != SpecialType.None
            || type.TypeKind == TypeKind.Enum
            || (type.IsValueType && type.ContainingNamespace is { Name: "System", ContainingNamespace.IsGlobalNamespace: true }
                && type.Name is "Guid" or "DateTime" or "DateTimeOffset" or "DateOnly" or "TimeOnly" or "TimeSpan");
    }

    // The method a lambda or local function belongs to.
    private static IMethodSymbol? Enclosing(ISymbol symbol)
    {
        for (var current = symbol; current is not null; current = current.ContainingSymbol)
        {
            if (current is IMethodSymbol { MethodKind: MethodKind.Ordinary } method)
                return method;
            if (current is INamedTypeSymbol)
                return null;
        }
        return null;
    }

    private static bool ReturnsResultOf(IMethodSymbol method, INamedTypeSymbol type)
    {
        var returned = ResultTypes.IsAwaitable(method.ReturnType, out var inner) ? inner : method.ReturnType;
        return ResultTypes.IsResult(returned)
            && returned is INamedTypeSymbol { TypeArguments.Length: 1 } result
            && SymbolEqualityComparer.Default.Equals(result.TypeArguments[0], type);
    }

    // ---- RU0008 ---------------------------------------------------------------------------------------------------

    private static void AnalyzeMethod(SymbolAnalysisContext context)
    {
        var method = (IMethodSymbol)context.Symbol;
        if (method.MethodKind is not (MethodKind.Ordinary or MethodKind.PropertyGet) || method.IsOverride || method.IsImplicitlyDeclared
            || ImplementsInterface(method))
            return;

        Report(context.ReportDiagnostic, method);
    }

    private static void AnalyzeLocalFunction(OperationAnalysisContext context) =>
        Report(context.ReportDiagnostic, ((ILocalFunctionOperation)context.Operation).Symbol);

    private static void Report(System.Action<Diagnostic> report, IMethodSymbol method)
    {
        if (method.ReturnType is not INamedTypeSymbol { Name: "ValueTask", TypeArguments.Length: 1 } valueTask
            || !ResultTypes.IsAwaitable(valueTask, out var inner) || !ResultTypes.IsResult(inner))
            return;

        var location = method.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() switch
        {
            MethodDeclarationSyntax declaration => declaration.ReturnType.GetLocation(),
            LocalFunctionStatementSyntax local => local.ReturnType.GetLocation(),
            PropertyDeclarationSyntax property => property.Type.GetLocation(),
            AccessorDeclarationSyntax { Parent.Parent: PropertyDeclarationSyntax property } => property.Type.GetLocation(),
            _ => method.Locations.FirstOrDefault(),
        };

        report(Diagnostic.Create(Descriptors.ValueTaskResult, location, method.MethodKind == MethodKind.PropertyGet
            ? method.AssociatedSymbol?.Name ?? method.Name
            : method.Name, valueTask.ToDisplayString(TypeFormat)));
    }

    private static bool ImplementsInterface(IMethodSymbol method) =>
        method.ExplicitInterfaceImplementations.Length > 0
        || method.ContainingType.AllInterfaces.SelectMany(i => i.GetMembers().OfType<IMethodSymbol>())
            .Any(member => SymbolEqualityComparer.Default.Equals(method.ContainingType.FindImplementationForInterfaceMember(member), method));

    // ---- shared ---------------------------------------------------------------------------------------------------

    // Inside a type named Errors, or whose name ends in "Errors" (UserErrors), at any depth: the catalog convention.
    private static bool IsInCatalog(ISymbol? symbol)
    {
        for (var type = symbol as INamedTypeSymbol ?? symbol?.ContainingType; type is not null; type = type.ContainingType)
        {
            if (type.Name.EndsWith("Errors", System.StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private static bool IsErrorType(ITypeSymbol? type) =>
        type is INamedTypeSymbol { Name: "Error", ContainingNamespace: { Name: "ResultUnion", ContainingNamespace.IsGlobalNamespace: true } };
}
