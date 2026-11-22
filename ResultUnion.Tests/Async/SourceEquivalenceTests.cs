using System.Linq.Expressions;
using System.Reflection;

namespace ResultUnion.Tests.Async;

/// <summary>
/// Every pipeline method on a <see cref="Task{TResult}"/> or <see cref="ValueTask{TResult}"/> of a result behaves the
/// same whatever the source: a completed task (the fast paths), a pending task (the async paths) or a ValueTask,
/// completed or pending.
/// </summary>
/// <remarks>
/// <para>
/// The reference is the method called on a completed <see cref="Task{TResult}"/>, which the hand-written tests cover;
/// a ValueTask method is compared with its Task overload. Each method runs with a successful and a failed source and
/// with delegates that answer "yes" (true, a success) and "no" (false, a failure); the outcome is the awaited value or
/// exception and the delegates called, in order, with their arguments.
/// </para>
/// <para>
/// The methods are found by reflection, so an overload added later is covered without a new test. Coverage showed
/// that most overloads were only ever called on completed tasks: a pending source or a ValueTask could have called the
/// wrong delegate, or dropped an argument, unnoticed.
/// </para>
/// </remarks>
public sealed class SourceEquivalenceTests
{
    private static readonly Error Failed = Error.NotFound("Missing.");
    private static readonly Error Invalid = Error.Validation("Invalid.");
    private static readonly Error Other = Error.Conflict("Other.");

    public static TheoryData<string> Cases()
    {
        var data = new TheoryData<string>();
        foreach (var method in Targets())
            data.Add(Describe(method));
        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Pending_and_ValueTask_sources_behave_like_a_completed_task(string description)
    {
        var method = Targets().Single(m => Describe(m) == description);
        var source = method.GetParameters()[0].ParameterType;
        var isValueTask = source.GetGenericTypeDefinition() == typeof(ValueTask<>);
        var reference = isValueTask ? TaskOverload(method) ?? method : method;

        foreach (var succeeds in (bool[])[true, false])
        {
            foreach (var answer in (bool[])[true, false])
            {
                var expected = await Run(reference, Source.Completed, succeeds, answer);

                foreach (var kind in isValueTask ? (Source[])[Source.Completed, Source.Pending] : [Source.Pending])
                {
                    var actual = await Run(method, kind, succeeds, answer);
                    Assert.True(expected == actual,
                        $"{description} on a {kind} source (succeeds: {succeeds}, delegates answer: {answer}):\n" +
                        $"expected {expected}\n" +
                        $"but got  {actual}");
                }
            }
        }
    }

    [Fact]
    public void The_contract_covers_the_Task_and_ValueTask_overloads()
    {
        var targets = Targets().ToList();
        var valueTasks = targets.Count(m => m.GetParameters()[0].ParameterType.GetGenericTypeDefinition() == typeof(ValueTask<>));

        Assert.True(targets.Count - valueTasks >= 40, $"Only {targets.Count - valueTasks} Task overloads were found; the method discovery is broken.");
        Assert.True(valueTasks >= 40, $"Only {valueTasks} ValueTask overloads were found; the method discovery is broken.");

        // A method on a result source that the contract cannot call (a parameter type Value does not know) is not
        // skipped silently.
        var all = typeof(ResultAsyncExtensions).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => !m.Name.Contains('<') && m.GetParameters().Length > 0)
            .Select(m => m.IsGenericMethodDefinition ? CloseOverInt(m) : m)
            .OfType<MethodInfo>()
            .Where(m => IsResultSource(m.GetParameters()[0].ParameterType))
            .Select(Describe);
        Assert.Empty(all.Except(targets.Select(Describe)));
    }

    // ---- running a method --------------------------------------------------------------------------------------------

    private enum Source { Completed, Pending }

    private static async Task<string> Run(MethodInfo method, Source kind, bool succeeds, bool answer)
    {
        var calls = new List<string>();
        var parameters = method.GetParameters();
        var arguments = new object?[parameters.Length];
        for (var i = 1; i < parameters.Length; i++)
            arguments[i] = Argument(parameters[i], answer, calls);
        var (source, complete) = MakeSource(parameters[0].ParameterType, kind, succeeds);
        arguments[0] = source;

        string outcome;
        try
        {
            var task = (Task)method.Invoke(null, arguments)!;
            complete(); // a pending source completes only after the call, so the call cannot take a fast path
            await task;
            outcome = method.ReturnType.IsGenericType
                ? $"{method.ReturnType.GetProperty(nameof(Task<int>.Result))!.GetValue(task)}"
                : "completed";
        }
        catch (Exception exception)
        {
            outcome = $"threw {(exception is TargetInvocationException { InnerException: { } inner } ? inner : exception).GetType().Name}";
        }

        return $"{outcome}; calls: [{string.Join(", ", calls)}]";
    }

    private static (object Source, Action Complete) MakeSource(Type sourceType, Source kind, bool succeeds)
    {
        var resultType = sourceType.GetGenericArguments()[0];
        var isValueTask = sourceType.GetGenericTypeDefinition() == typeof(ValueTask<>);
        var factory = typeof(SourceEquivalenceTests).GetMethod(nameof(MakeSource), 1, BindingFlags.NonPublic | BindingFlags.Static,
            [Type.MakeGenericMethodParameter(0), typeof(bool), typeof(bool)])!.MakeGenericMethod(resultType);
        return ((object, Action))factory.Invoke(null, [Value(resultType, succeeds), isValueTask, kind == Source.Pending])!;
    }

    private static (object Source, Action Complete) MakeSource<T>(T value, bool isValueTask, bool pending)
    {
        if (!pending)
            return (isValueTask ? ValueTask.FromResult(value) : Task.FromResult(value), static () => { });

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        return (isValueTask ? new ValueTask<T>(completion.Task) : completion.Task, () => completion.SetResult(value));
    }

    private static object? Argument(ParameterInfo parameter, bool answer, List<string> calls)
    {
        var type = parameter.ParameterType;
        if (!typeof(Delegate).IsAssignableFrom(type))
            return Value(type, answer);

        // A delegate that records the call and its arguments, then returns the answer for its return type.
        var invoke = type.GetMethod(nameof(Action.Invoke))!;
        var inputs = invoke.GetParameters().Select(p => Expression.Parameter(p.ParameterType, p.Name)).ToArray();
        var record = Expression.Call(
            Expression.Constant(calls),
            typeof(List<string>).GetMethod(nameof(List<string>.Add))!,
            Expression.Call(
                typeof(SourceEquivalenceTests).GetMethod(nameof(DescribeCall), BindingFlags.NonPublic | BindingFlags.Static)!,
                Expression.Constant(parameter.Name),
                Expression.NewArrayInit(typeof(object), inputs.Select(i => Expression.Convert(i, typeof(object))))));
        Expression body = invoke.ReturnType == typeof(void)
            ? record
            : Expression.Block(record, Expression.Constant(Value(invoke.ReturnType, answer), invoke.ReturnType));
        return Expression.Lambda(type, body, inputs).Compile();
    }

    private static string DescribeCall(string name, object[] arguments) => $"{name}({string.Join(", ", arguments)})";

    // The value of a type for a "yes" or "no" answer (for a source: a success or a failure).
    private static object? Value(Type type, bool yes)
    {
        if (type == typeof(bool))
            return yes;
        if (type == typeof(int))
            return yes ? 5 : 8;
        if (type == typeof(Error))
            return yes ? Invalid : Other;
        if (type == typeof(Result))
            return yes ? Result.Success() : Result.Failure(Failed);
        if (type == typeof(Result<int>))
            return yes ? Result<int>.Success(5) : Result<int>.Failure(Failed);
        if (type == typeof(Task))
            return Task.CompletedTask;
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>))
        {
            var inner = type.GetGenericArguments()[0];
            return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(inner).Invoke(null, [Value(inner, yes)]);
        }
        throw new NotSupportedException(type.Name);
    }

    // ---- finding the methods -----------------------------------------------------------------------------------------

    private static IEnumerable<MethodInfo> Targets() =>
        typeof(ResultAsyncExtensions).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => !m.Name.Contains('<') && m.GetParameters().Length > 0)
            .Select(m => m.IsGenericMethodDefinition ? CloseOverInt(m) : m)
            .OfType<MethodInfo>()
            .Where(m => IsResultSource(m.GetParameters()[0].ParameterType) && m.GetParameters().Skip(1).All(p => Supported(p.ParameterType)))
            .OrderBy(Describe, StringComparer.Ordinal);

    // The value types are int, the type parameters too; a method whose constraints rule int out (a reference type, an
    // exception) has no result source.
    private static MethodInfo? CloseOverInt(MethodInfo method)
    {
        try
        {
            return method.MakeGenericMethod([.. method.GetGenericArguments().Select(_ => typeof(int))]);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static bool IsResultSource(Type type) =>
        type.IsGenericType
        && type.GetGenericTypeDefinition() is var definition && (definition == typeof(Task<>) || definition == typeof(ValueTask<>))
        && type.GetGenericArguments()[0] is var result && (result == typeof(Result) || result == typeof(Result<int>));

    private static bool Supported(Type type)
    {
        if (!typeof(Delegate).IsAssignableFrom(type))
            return CanMake(type);
        var invoke = type.GetMethod(nameof(Action.Invoke))!;
        return invoke.ReturnType == typeof(void) || CanMake(invoke.ReturnType);

        static bool CanMake(Type t)
        {
            try
            {
                _ = Value(t, yes: true);
                return true;
            }
            catch (NotSupportedException)
            {
                return false;
            }
        }
    }

    private static MethodInfo? TaskOverload(MethodInfo valueTaskMethod)
    {
        var parameters = valueTaskMethod.GetParameters().Select(p => p.ParameterType).ToArray();
        parameters[0] = typeof(Task<>).MakeGenericType(parameters[0].GetGenericArguments());
        return Targets().SingleOrDefault(m => m.Name == valueTaskMethod.Name
            && m.ReturnType == valueTaskMethod.ReturnType
            && m.GetParameters().Select(p => p.ParameterType).SequenceEqual(parameters));
    }

    private static string Describe(MethodInfo method) =>
        $"{method.Name}{(method.IsGenericMethod ? $"<{string.Join(", ", method.GetGenericArguments().Select(Name))}>" : "")}" +
        $"({string.Join(", ", method.GetParameters().Select(p => Name(p.ParameterType)))})";

    private static string Name(Type type) =>
        type.IsGenericType
            ? $"{type.Name[..type.Name.IndexOf('`')]}<{string.Join(", ", type.GetGenericArguments().Select(Name))}>"
            : type.Name;
}
