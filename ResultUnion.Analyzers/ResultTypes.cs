using Microsoft.CodeAnalysis;

namespace ResultUnion.Analyzers;

/// <summary>Recognizes the library's types by name, so the analyzer needs no reference to ResultUnion itself.</summary>
internal static class ResultTypes
{
    private const string Namespace = "ResultUnion";

    /// <summary><c>Result</c> or <c>Result&lt;T&gt;</c> from the ResultUnion namespace.</summary>
    public static bool IsResult(ITypeSymbol? type) =>
        type is INamedTypeSymbol { Name: "Result" } named
        && named.TypeArguments.Length <= 1
        && IsInResultUnionNamespace(named);

    /// <summary>A result, or a <c>Task</c> / <c>ValueTask</c> of a result.</summary>
    public static bool IsResultOrTaskOfResult(ITypeSymbol? type) =>
        IsResult(type) || (IsAwaitable(type, out var inner) && IsResult(inner));

    /// <summary><c>Task</c>, <c>Task&lt;T&gt;</c>, <c>ValueTask</c> or <c>ValueTask&lt;T&gt;</c>; <paramref name="inner"/> is the T.</summary>
    public static bool IsAwaitable(ITypeSymbol? type, out ITypeSymbol? inner)
    {
        inner = null;
        if (type is not INamedTypeSymbol { Name: "Task" or "ValueTask" } named
            || named.ContainingNamespace?.ToDisplayString() != "System.Threading.Tasks")
            return false;

        if (named.TypeArguments.Length == 1)
            inner = named.TypeArguments[0];
        return true;
    }

    /// <summary>A member declared by the library: on a result type, or in one of its extension classes.</summary>
    public static bool IsLibraryMember(ISymbol symbol)
    {
        for (var type = symbol.ContainingType; type is not null; type = type.ContainingType)
        {
            if (IsInResultUnionNamespace(type))
                return true;
        }
        return false;
    }

    private static bool IsInResultUnionNamespace(ISymbol symbol) =>
        symbol.ContainingNamespace is { Name: Namespace, ContainingNamespace.IsGlobalNamespace: true };
}
