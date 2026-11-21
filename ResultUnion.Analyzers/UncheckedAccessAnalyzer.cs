using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace ResultUnion.Analyzers;

/// <summary>
/// RU0004: <c>ResultValue</c> or <c>Error</c> read where the result's state is not known.
/// </summary>
/// <remarks>
/// <para>
/// The check is local and conservative: a read is accepted when an enclosing <c>if</c>, <c>?:</c>, <c>&amp;&amp;</c> or
/// <c>||</c>, or an earlier <c>if</c> in the same block whose branch always exits, establishes the needed state through
/// <c>IsSuccess</c>, <c>IsFailure</c>, <c>TryGetResultValue</c> or <c>TryGetError</c> on the same result. The same result
/// means the same local, parameter, field or property (through such a chain), or <c>this</c>. A read on anything else,
/// such as a method call's result, is always reported: it cannot have been checked.
/// </para>
/// <para>
/// It does not track reassignments between the check and the read, and does not look through other control flow
/// (loops, switch, try): those reads are simply not reported or reported, never guessed. Missing a rare guard reports a
/// read that is in fact safe; the fix (switch, Match, TryGetResultValue) is still the clearer code.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UncheckedAccessAnalyzer : DiagnosticAnalyzer
{
    private enum State
    {
        Success,
        Failure,
    }

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Descriptors.UncheckedAccess);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(AnalyzePropertyReference, OperationKind.PropertyReference);
    }

    private static void AnalyzePropertyReference(OperationAnalysisContext context)
    {
        var reference = (IPropertyReferenceOperation)context.Operation;
        var property = reference.Property;

        if (property.ContainingType is not { } type || !ResultTypes.IsResult(type))
            return;

        State needed;
        switch (property.Name)
        {
            case "ResultValue":
                needed = State.Success;
                break;
            case "Error":
                needed = State.Failure;
                break;
            default:
                return;
        }

        // Code inside the result types themselves reads its own state directly; it is not a consumer.
        if (IsInsideResultType(context.ContainingSymbol))
            return;

        var key = ReceiverKey(reference.Instance);
        if (key is not null && IsGuarded(reference, key, needed))
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            Descriptors.UncheckedAccess,
            reference.Syntax.GetLocation(),
            reference.Syntax.ToString(),
            needed == State.Success ? "success" : "failure",
            needed == State.Success ? "TryGetResultValue" : "TryGetError",
            needed == State.Success ? "IsSuccess" : "IsFailure"));
    }

    // ---- guards ----------------------------------------------------------------------------------------------------

    private static bool IsGuarded(IOperation access, string key, State needed)
    {
        var current = access;

        for (var parent = current.Parent; parent is not null; current = parent, parent = parent.Parent)
        {
            switch (parent)
            {
                // if / ?: — the read is in a branch whose condition establishes the state.
                case IConditionalOperation conditional:
                    if (ReferenceEquals(current, conditional.WhenTrue) && Implies(conditional.Condition, key, whenTrue: true) == needed)
                        return true;
                    if (ReferenceEquals(current, conditional.WhenFalse) && Implies(conditional.Condition, key, whenTrue: false) == needed)
                        return true;
                    break;

                // a && b, a || b — the right side runs only when the left side is true (&&) or false (||).
                case IBinaryOperation { OperatorKind: BinaryOperatorKind.ConditionalAnd } conjunction:
                    if (ReferenceEquals(current, conjunction.RightOperand) && Implies(conjunction.LeftOperand, key, whenTrue: true) == needed)
                        return true;
                    break;
                case IBinaryOperation { OperatorKind: BinaryOperatorKind.ConditionalOr } disjunction:
                    if (ReferenceEquals(current, disjunction.RightOperand) && Implies(disjunction.LeftOperand, key, whenTrue: false) == needed)
                        return true;
                    break;

                // An earlier statement in the same block: if (result.IsFailure) return ...;
                case IBlockOperation block:
                    if (IsGuardedByEarlierExit(block, current, key, needed))
                        return true;
                    break;
            }
        }

        return false;
    }

    private static bool IsGuardedByEarlierExit(IBlockOperation block, IOperation statement, string key, State needed)
    {
        var index = block.Operations.IndexOf(statement);

        for (var i = index - 1; i >= 0; i--)
        {
            if (block.Operations[i] is not IConditionalOperation conditional)
                continue;

            // if (wrong state) exit;  — afterwards the condition is known to be false.
            if (AlwaysExits(conditional.WhenTrue) && Implies(conditional.Condition, key, whenTrue: false) == needed)
                return true;

            // if (right state) { ... } else exit;  — afterwards the condition is known to be true.
            if (conditional.WhenFalse is not null && AlwaysExits(conditional.WhenFalse)
                && Implies(conditional.Condition, key, whenTrue: true) == needed)
                return true;
        }

        return false;
    }

    private static bool AlwaysExits(IOperation? operation) => operation switch
    {
        null => false,
        IReturnOperation or IThrowOperation or IBranchOperation => true,
        IExpressionStatementOperation { Operation: IThrowOperation } => true,
        IBlockOperation { Operations.Length: > 0 } block => AlwaysExits(block.Operations[block.Operations.Length - 1]),
        IConditionalOperation { WhenFalse: not null } branch => AlwaysExits(branch.WhenTrue) && AlwaysExits(branch.WhenFalse),
        _ => false,
    };

    /// <summary>The state of the result <paramref name="key"/> when <paramref name="condition"/> has the given value.</summary>
    private static State? Implies(IOperation condition, string key, bool whenTrue)
    {
        switch (Unwrap(condition))
        {
            case IPropertyReferenceOperation { Property.Name: "IsSuccess" or "IsFailure" } check
                when ResultTypes.IsResult(check.Property.ContainingType) && ReceiverKey(check.Instance) == key:
                var isSuccessCheck = check.Property.Name == "IsSuccess";
                return isSuccessCheck == whenTrue ? State.Success : State.Failure;

            case IUnaryOperation { OperatorKind: UnaryOperatorKind.Not } negation:
                return Implies(negation.Operand, key, !whenTrue);

            case IBinaryOperation { OperatorKind: BinaryOperatorKind.ConditionalAnd } conjunction when whenTrue:
                return Implies(conjunction.LeftOperand, key, true) ?? Implies(conjunction.RightOperand, key, true);

            case IBinaryOperation { OperatorKind: BinaryOperatorKind.ConditionalOr } disjunction when !whenTrue:
                return Implies(disjunction.LeftOperand, key, false) ?? Implies(disjunction.RightOperand, key, false);

            // result.IsSuccess == true / != false, and the mirror images.
            case IBinaryOperation { OperatorKind: BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals } comparison:
            {
                IOperation other;
                bool literal;
                if (BoolLiteral(comparison.RightOperand) is bool right)
                {
                    other = comparison.LeftOperand;
                    literal = right;
                }
                else if (BoolLiteral(comparison.LeftOperand) is bool left)
                {
                    other = comparison.RightOperand;
                    literal = left;
                }
                else
                {
                    return null;
                }

                var equals = comparison.OperatorKind == BinaryOperatorKind.Equals;
                return Implies(other, key, whenTrue == (equals == literal));
            }

            // result.TryGetResultValue(out ...) / TryGetError(out ...) / the union's TryGetValue overloads.
            case IInvocationOperation invocation
                when invocation.TargetMethod.Name is "TryGetResultValue" or "TryGetError" or "TryGetValue"
                     && ResultTypes.IsResult(invocation.TargetMethod.ContainingType)
                     && ReceiverKey(invocation.Instance) == key:
            {
                var meansSuccess = invocation.TargetMethod.Name switch
                {
                    "TryGetResultValue" => true,
                    "TryGetError" => false,
                    _ => invocation.TargetMethod.Parameters.FirstOrDefault()?.Type.Name != "Error",
                };
                return meansSuccess == whenTrue ? State.Success : State.Failure;
            }

            default:
                return null;
        }
    }

    private static bool? BoolLiteral(IOperation operation) =>
        Unwrap(operation) is ILiteralOperation { ConstantValue: { HasValue: true, Value: bool value } } ? value : null;

    // ---- the identity of a result ----------------------------------------------------------------------------------

    /// <summary>
    /// A stable name for the result a member is read from: the same local, parameter, field or property chain, or
    /// <c>this</c>; <see langword="null"/> for anything that cannot have been checked, such as a call's result.
    /// </summary>
    private static string? ReceiverKey(IOperation? instance)
    {
        switch (Unwrap(instance))
        {
            case null:
                return null;
            case IInstanceReferenceOperation:
                return "this";
            case ILocalReferenceOperation local:
                return "local:" + local.Local.Name + "@" + local.Local.Locations.FirstOrDefault()?.SourceSpan.Start;
            case IParameterReferenceOperation parameter:
                return "parameter:" + parameter.Parameter.Name;
            case IFieldReferenceOperation field:
                return MemberKey(field.Instance, "field:" + field.Field.ToDisplayString());
            case IPropertyReferenceOperation property when property.Arguments.IsEmpty:
                return MemberKey(property.Instance, "property:" + property.Property.ToDisplayString());
            default:
                return null;
        }
    }

    private static string? MemberKey(IOperation? instance, string member)
    {
        if (instance is null)
            return member;   // static

        var owner = ReceiverKey(instance);
        return owner is null ? null : owner + "." + member;
    }

    private static IOperation? Unwrap(IOperation? operation)
    {
        while (operation is IConversionOperation { IsImplicit: true } conversion)
            operation = conversion.Operand;
        return operation;
    }

    private static bool IsInsideResultType(ISymbol? symbol)
    {
        for (var type = symbol as INamedTypeSymbol ?? symbol?.ContainingType; type is not null; type = type.ContainingType)
        {
            if (ResultTypes.IsResult(type))
                return true;
        }
        return false;
    }
}
