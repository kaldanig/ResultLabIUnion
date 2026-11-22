using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace ResultUnion.Analyzers;

/// <summary>
/// RU0010: a result written as or read from JSON, where it should be mapped at the boundary. Recognizes Minimal API
/// handlers and the JSON APIs of System.Text.Json, ASP.NET Core and System.Net.Http.Json by name, so the analyzer
/// needs no reference to them.
/// </summary>
[DiagnosticAnalyzer(Microsoft.CodeAnalysis.LanguageNames.CSharp)]
public sealed class ResultJsonAnalyzer : DiagnosticAnalyzer
{
    private static readonly SymbolDisplayFormat TypeFormat = SymbolDisplayFormat.MinimallyQualifiedFormat;

    // EndpointRouteBuilderExtensions methods whose Delegate argument becomes the request handler.
    private static readonly ImmutableHashSet<string> MapMethods = ImmutableHashSet.Create(
        "Map", "MapGet", "MapPost", "MapPut", "MapDelete", "MapPatch", "MapMethods", "MapFallback");

    // Types whose methods write their arguments as JSON or read their return value from it: (namespace, type name).
    private static readonly ImmutableHashSet<(string, string)> JsonTypes = ImmutableHashSet.Create(
        ("System.Text.Json", "JsonSerializer"),
        ("Microsoft.AspNetCore.Http", "Results"),
        ("Microsoft.AspNetCore.Http", "TypedResults"),
        ("Microsoft.AspNetCore.Http", "HttpResponseJsonExtensions"),
        ("Microsoft.AspNetCore.Http", "HttpRequestJsonExtensions"),
        ("System.Net.Http.Json", "HttpClientJsonExtensions"),
        ("System.Net.Http.Json", "HttpContentJsonExtensions"),
        ("System.Net.Http.Json", "JsonContent"));

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Descriptors.SerializedResult);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;

        if (IsEndpointMap(method))
        {
            foreach (var argument in invocation.Arguments.Where(a => a.Parameter?.Type is { Name: "Delegate", ContainingNamespace.Name: "System" }))
            {
                if (HandlerReturnType(argument.Value) is { } returned && IsResultLike(returned))
                    Report(context, argument.Syntax.GetLocation(), $"The endpoint handler returns {Display(returned)} as the response body");
            }
            return;
        }

        if (method.ContainingType is not { } type || !JsonTypes.Contains((type.ContainingNamespace?.ToDisplayString() ?? "", type.Name)))
            return;

        var name = $"'{type.Name}.{method.Name}'";

        // JsonSerializer.Deserialize<Result<int>>, GetFromJsonAsync<Result<int>>, ReadFromJsonAsync<Result<int>>.
        if (IsResultLike(invocation.Type))
        {
            Report(context, invocation.Syntax.GetLocation(), $"{name} reads {Display(invocation.Type!)} from JSON");
            return;
        }

        foreach (var argument in invocation.Arguments)
        {
            var value = Unwrap(argument.Value);
            if (IsResultLike(value.Type))
                Report(context, argument.Syntax.GetLocation(), $"{name} writes {Display(value.Type!)} as JSON");
        }
    }

    private static void Report(OperationAnalysisContext context, Location location, string what) =>
        context.ReportDiagnostic(Diagnostic.Create(Descriptors.SerializedResult, location, what));

    private static bool IsEndpointMap(IMethodSymbol method) =>
        MapMethods.Contains(method.Name)
        && method.ContainingType is { Name: "EndpointRouteBuilderExtensions" } type
        && type.ContainingNamespace?.ToDisplayString() == "Microsoft.AspNetCore.Builder";

    // The return type of a lambda or method group passed as a Delegate.
    private static ITypeSymbol? HandlerReturnType(IOperation value) => Unwrap(value) switch
    {
        IDelegateCreationOperation { Target: var target } => HandlerReturnType(target),
        IAnonymousFunctionOperation function => function.Symbol.ReturnType,
        IMethodReferenceOperation reference => reference.Method.ReturnType,
        _ => null,
    };

    // A result, a task of one, or a nullable one (GetFromJsonAsync<Result<int>> returns Task<Result<int>?>).
    private static bool IsResultLike(ITypeSymbol? type)
    {
        if (ResultTypes.IsAwaitable(type, out var inner))
            type = inner;
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
            type = nullable.TypeArguments[0];
        return ResultTypes.IsResult(type);
    }

    private static string Display(ITypeSymbol type) => type.ToDisplayString(TypeFormat);

    private static IOperation Unwrap(IOperation operation)
    {
        while (operation is IConversionOperation { IsImplicit: true } conversion)
            operation = conversion.Operand;
        return operation;
    }
}
