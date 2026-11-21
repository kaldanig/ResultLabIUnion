using System.Linq.Expressions;
using System.Reflection;

namespace ResultUnion.Tests.Contracts;

/// <summary>
/// Checks, for every public method of an assembly, that each argument that must not be <see langword="null"/> is
/// rejected with <see cref="ArgumentNullException"/> naming it, <em>when the method is called</em> (DESIGN.md, 11).
/// </summary>
/// <remarks>
/// <para>
/// One argument at a time is replaced by <see langword="null"/>; the others get valid values (a successful result, a
/// completed task, a delegate that returns a success). An exception stored in a returned task does not count: the
/// contract is that a wrong call fails at once, not when the task is awaited.
/// </para>
/// <para>
/// Arguments annotated as nullable are skipped, as are values of a generic type (the value of a result, which the
/// result's own tests cover). Generic methods are closed over <see cref="int"/>, or <see cref="string"/> when the
/// type parameter must be a reference type. Mutation testing found that removing most argument checks went unnoticed;
/// this test covers them all, including the members added later.
/// </para>
/// </remarks>
public static class NullArgumentContract
{
    private static readonly NullabilityInfoContext Nullability = new();

    /// <summary>One case per (method, argument) pair that must reject <see langword="null"/>.</summary>
    /// <param name="closeType">Chooses the type argument for a generic parameter the defaults cannot handle (a constraint), or <see langword="null"/>.</param>
    /// <param name="sample">Creates a valid value for a type the defaults do not know, or <see langword="null"/>.</param>
    public static TheoryData<string, string> Cases(Assembly assembly, Func<Type, Type?>? closeType = null, Func<Type, object?>? sample = null)
    {
        var data = new TheoryData<string, string>();
        foreach (var (method, parameter) in Targets(assembly, closeType, sample))
            data.Add(Describe(method), parameter.Name!);
        return data;
    }

    /// <summary>Calls the method with <paramref name="parameterName"/> set to <see langword="null"/> and checks the exception.</summary>
    public static void Verify(Assembly assembly, string method, string parameterName, Func<Type, Type?>? closeType = null, Func<Type, object?>? sample = null)
    {
        var (target, parameter) = Targets(assembly, closeType, sample).Single(t => Describe(t.Method) == method && t.Parameter.Name == parameterName);

        // Twice: with completed tasks (the fast paths) and with pending ones (the async paths), because a fast path
        // may pass the argument on to a method that checks it, which would hide a missing check on the async path.
        foreach (var pending in (bool[])[false, true])
        {
            var arguments = target.GetParameters()
                .Select(p => p.Position == parameter.Position ? null : Sample(p.ParameterType, sample, pending))
                .ToArray();
            var instance = target.IsStatic ? null : Sample(target.DeclaringType!, sample, pending);

            var thrown = Record.Exception(() => { _ = target.Invoke(instance, arguments); }); // not awaited: the exception must come first

            var inner = Assert.IsType<TargetInvocationException>(thrown).InnerException;
            var exception = Assert.IsType<ArgumentNullException>(inner, exactMatch: false);
            Assert.Equal(parameterName, exception.ParamName);
        }
    }

    private static IEnumerable<(MethodInfo Method, ParameterInfo Parameter)> Targets(
        Assembly assembly, Func<Type, Type?>? closeType, Func<Type, object?>? sample)
    {
        foreach (var type in assembly.GetExportedTypes().Where(t => !t.IsInterface && !t.Name.Contains('<')).OrderBy(t => t.FullName))
        {
            var closed = type.IsGenericTypeDefinition ? CloseType(type, closeType) : type;
            if (closed is null)
                continue;

            var methods = closed.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => !m.IsSpecialName && !m.Name.Contains('<') && m.Name is not ("Equals" or "GetHashCode" or "ToString" or "Deconstruct"))
                .Where(m => m.IsStatic || !closed.IsAbstract)
                .OrderBy(Describe, StringComparer.Ordinal);

            foreach (var open in methods)
            {
                var method = open.IsGenericMethodDefinition ? CloseMethod(open, closeType) : open;
                if (method is null || method.GetParameters().Any(p => p.ParameterType.IsByRefLike || p.ParameterType.IsPointer))
                    continue;
                if (!method.GetParameters().All(p => p.IsOut || CanSample(p.ParameterType, sample)))
                    continue;
                if (!method.IsStatic && !CanSample(closed, sample))
                    continue;

                var openParameters = open.GetParameters();
                foreach (var parameter in method.GetParameters())
                {
                    var declared = openParameters[parameter.Position];
                    if (parameter.IsOut || parameter.ParameterType.IsValueType || declared.ParameterType.IsGenericParameter)
                        continue;
                    if (Nullability.Create(declared).WriteState != NullabilityState.NotNull)
                        continue;

                    yield return (method, parameter);
                }
            }
        }
    }

    private static string Describe(MethodInfo method) =>
        $"{Name(method.DeclaringType!)}.{method.Name}{(method.IsGenericMethod ? $"<{string.Join(", ", method.GetGenericArguments().Select(Name))}>" : "")}" +
        $"({string.Join(", ", method.GetParameters().Select(p => Name(p.ParameterType)))})";

    private static string Name(Type type) =>
        type.IsGenericType
            ? $"{type.Name[..type.Name.IndexOf('`')]}<{string.Join(", ", type.GetGenericArguments().Select(Name))}>"
            : type.Name;

    private static Type? CloseType(Type type, Func<Type, Type?>? closeType)
    {
        var arguments = type.GetGenericArguments().Select(a => closeType?.Invoke(a) ?? Argument(a)).ToArray();
        return arguments.Any(a => a is null) ? null : type.MakeGenericType(arguments!);
    }

    private static MethodInfo? CloseMethod(MethodInfo method, Func<Type, Type?>? closeType)
    {
        var arguments = method.GetGenericArguments().Select(a => closeType?.Invoke(a) ?? Argument(a)).ToArray();
        if (arguments.Any(a => a is null))
            return null;
        try
        {
            return method.MakeGenericMethod(arguments!);
        }
        catch (ArgumentException)
        {
            return null; // a constraint the simple choice does not meet
        }
    }

    private static Type? Argument(Type parameter)
    {
        var constraints = parameter.GetGenericParameterConstraints();
        if (constraints.Any(c => c == typeof(Exception) || c.IsSubclassOf(typeof(Exception))))
            return typeof(InvalidOperationException);
        if (constraints.Any(c => c.IsGenericType && c.GetGenericTypeDefinition() == typeof(IResultFactory<>)))
            return typeof(Result);
        if (constraints.Length == 1 && constraints[0] == typeof(ValueType))
            return typeof(int); // where T : struct
        if (constraints.Length > 0)
            return null;
        return parameter.GenericParameterAttributes.HasFlag(GenericParameterAttributes.ReferenceTypeConstraint)
            ? typeof(string)
            : typeof(int);
    }

    private static bool CanSample(Type type, Func<Type, object?>? sample)
    {
        try
        {
            _ = Sample(type, sample, pending: false);
            return true;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>A valid, non-null value of <paramref name="type"/>; tasks are completed, or never complete when <paramref name="pending"/> is set.</summary>
    private static object? Sample(Type type, Func<Type, object?>? sample, bool pending)
    {
        if (sample?.Invoke(type) is { } custom)
            return custom;
        if (type == typeof(int))
            return 1;
        if (type == typeof(string))
            return "value";
        if (type == typeof(Error))
            return Error.Validation("Invalid.");
        if (type == typeof(ErrorType))
            return ErrorType.Validation;
        if (type == typeof(Result))
            return Result.Success();
        if (type == typeof(Exception) || type == typeof(InvalidOperationException))
            return new InvalidOperationException("Failed.");
        if (type == typeof(CancellationToken))
            return CancellationToken.None;
        if (type == typeof(object))
            return new object();
        if (type == typeof(bool))
            return true;
        if (type == typeof(Task))
            return pending ? new TaskCompletionSource().Task : Task.CompletedTask;
        if (type.IsGenericType)
        {
            var definition = type.GetGenericTypeDefinition();
            var argument = type.GetGenericArguments()[0];
            if (definition == typeof(Result<>))
                return type.GetMethod(nameof(Result<int>.Success))!.Invoke(null, [Sample(argument, sample, pending)]);
            if (definition == typeof(Task<>))
                return pending
                    ? typeof(TaskCompletionSource<>).MakeGenericType(argument).GetProperty("Task")!.GetValue(Activator.CreateInstance(typeof(TaskCompletionSource<>).MakeGenericType(argument)))
                    : typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(argument).Invoke(null, [Sample(argument, sample, pending)]);
            if (definition == typeof(ValueTask<>))
                return pending
                    ? Activator.CreateInstance(type, Sample(typeof(Task<>).MakeGenericType(argument), sample, pending))
                    : Activator.CreateInstance(type, Sample(argument, sample, pending));
            if (definition == typeof(Nullable<>))
                return Sample(argument, sample, pending);
            if (definition == typeof(IEnumerable<>) || definition == typeof(IReadOnlyList<>) || definition == typeof(IReadOnlyCollection<>))
            {
                var array = Array.CreateInstance(argument, 1);
                array.SetValue(Sample(argument, sample, pending), 0);
                return array;
            }
            if (definition == typeof(KeyValuePair<,>))
                return Activator.CreateInstance(type, Sample(argument, sample, pending), Sample(type.GetGenericArguments()[1], sample, pending));
            if (type.FullName?.StartsWith("System.ValueTuple`", StringComparison.Ordinal) == true)
                return Activator.CreateInstance(type, [.. type.GetGenericArguments().Select(a => Sample(a, sample, pending))]);
        }
        if (typeof(Delegate).IsAssignableFrom(type))
            return Delegate(type, sample, pending);

        throw new NotSupportedException(type.ToString());
    }

    /// <summary>A delegate of <paramref name="type"/> that ignores its arguments and returns a valid value.</summary>
    private static Delegate Delegate(Type type, Func<Type, object?>? sample, bool pending)
    {
        var invoke = type.GetMethod("Invoke")!;
        var parameters = invoke.GetParameters().Select(p => Expression.Parameter(p.ParameterType, p.Name)).ToArray();
        Expression body = invoke.ReturnType == typeof(void)
            ? Expression.Empty()
            : Expression.Constant(Sample(invoke.ReturnType, sample, pending), invoke.ReturnType);
        return Expression.Lambda(type, body, parameters).Compile();
    }
}
