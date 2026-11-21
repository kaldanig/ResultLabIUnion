namespace ResultUnion;

// Allocation-saving paths shared by the async extensions.
//
// With .NET 11 Runtime Async, an async method whose task the caller awaits directly does not materialize that task,
// so the ordinary async methods are already the cheapest way to produce a new result. A Task object exists only where
// it is a value: the source of the next step in a fluent chain, or the return of a delegate. These helpers therefore
// avoid creating task objects, never create completed ones in place of an async method:
//
//  - a step that leaves the result unchanged (Ensure that passes, Tap, TapError, MapError on a success) on a source
//    that has already completed returns the source task itself;
//  - a successful Result without a value uses one shared completed task;
//  - a step that calls an async delegate returns that delegate's task instead of awaiting it inside another task.
//
// Every path keeps the semantics of the async method it replaces: an exception thrown by a delegate is delivered
// through the returned task (an OperationCanceledException as a canceled task), never thrown by the call itself, and a
// delegate that returns a null task faults the returned task with a NullReferenceException.
public static partial class ResultAsyncExtensions
{
    private static readonly Task<Result> CompletedSuccess = Task.FromResult(Result.Success());

    /// <summary>A completed task for <paramref name="result"/>; every success shares one task.</summary>
    private static Task<Result> Completed(Result result) =>
        result.IsSuccess ? CompletedSuccess : Task.FromResult(result);

    /// <summary>The task an async method would have returned had it thrown <paramref name="exception"/>.</summary>
    private static Task<TResult> Faulted<TResult>(Exception exception)
    {
        if (exception is OperationCanceledException canceled)
        {
            var completion = new TaskCompletionSource<TResult>();
            completion.SetCanceled(canceled.CancellationToken);
            return completion.Task;
        }

        return Task.FromException<TResult>(exception);   // the exception keeps the stack trace of its throw
    }

    // ---- synchronous steps on an already-completed source ----------------------------------------------------------

    /// <summary>Runs a step that produces a <see cref="Result"/> on a completed result; a success uses the shared task.</summary>
    private static Task<Result> RunCompletedResult<TIn, TState>(TIn input, TState state, Func<TIn, TState, Result> step)
    {
        try
        {
            return Completed(step(input, state));
        }
        catch (Exception exception)
        {
            return Faulted<Result>(exception);
        }
    }

    /// <summary>
    /// Runs a step that keeps the value (Ensure, Tap, TapError, MapError) on a completed source, and returns the source
    /// task itself when the outcome did not change.
    /// </summary>
    private static Task<Result<T>> KeepCompleted<T, TState>(
        Task<Result<T>> source, TState state, Func<Result<T>, TState, Result<T>> step)
        where T : notnull
    {
        var before = source.Result;
        Result<T> after;

        try
        {
            after = step(before, state);
        }
        catch (Exception exception)
        {
            return Faulted<Result<T>>(exception);
        }

        return Unchanged(before, after) ? source : Task.FromResult(after);
    }

    /// <summary>
    /// Runs a step on a completed <see cref="Result"/> source, and returns the source task itself when the outcome did
    /// not change (a result without a value is unchanged when its state and error are).
    /// </summary>
    private static Task<Result> KeepCompleted<TState>(Task<Result> source, TState state, Func<Result, TState, Result> step)
    {
        var before = source.Result;
        Result after;

        try
        {
            after = step(before, state);
        }
        catch (Exception exception)
        {
            return Faulted<Result>(exception);
        }

        return Unchanged(before, after) ? source : Completed(after);
    }

    // Only for value-preserving steps: a success that stays a success keeps its value.
    private static bool Unchanged<T>(Result<T> before, Result<T> after)
        where T : notnull =>
        before.TryGetError(out var beforeError)
            ? after.TryGetError(out var afterError) && ReferenceEquals(beforeError, afterError)
            : after.IsSuccess;

    private static bool Unchanged(Result before, Result after) =>
        before.TryGetError(out var beforeError)
            ? after.TryGetError(out var afterError) && ReferenceEquals(beforeError, afterError)
            : after.IsSuccess;

    // ---- asynchronous delegates ------------------------------------------------------------------------------------

    /// <summary>Calls <paramref name="next"/> and returns its task, as <c>await next(argument)</c> in an async method would.</summary>
    private static Task<TResult> Invoke<TArgument, TResult>(Func<TArgument, Task<TResult>> next, TArgument argument)
    {
        Task<TResult>? task;

        try
        {
            task = next(argument);
        }
        catch (Exception exception)
        {
            return Faulted<TResult>(exception);
        }

        return task ?? Faulted<TResult>(new NullReferenceException());
    }

    /// <summary>Calls <paramref name="next"/> and returns its task, as <c>await next()</c> in an async method would.</summary>
    private static Task<TResult> Invoke<TResult>(Func<Task<TResult>> next)
    {
        Task<TResult>? task;

        try
        {
            task = next();
        }
        catch (Exception exception)
        {
            return Faulted<TResult>(exception);
        }

        return task ?? Faulted<TResult>(new NullReferenceException());
    }

    /// <summary>Calls <paramref name="next"/> and returns its task, as <c>await next(argument)</c> in an async method would.</summary>
    private static Task InvokeAction<TArgument>(Func<TArgument, Task> next, TArgument argument)
    {
        Task? task;

        try
        {
            task = next(argument);
        }
        catch (Exception exception)
        {
            return Faulted<bool>(exception);
        }

        return task ?? Faulted<bool>(new NullReferenceException());
    }

    /// <summary>Calls <paramref name="next"/> and returns its task, as <c>await next()</c> in an async method would.</summary>
    private static Task InvokeAction(Func<Task> next)
    {
        Task? task;

        try
        {
            task = next();
        }
        catch (Exception exception)
        {
            return Faulted<bool>(exception);
        }

        return task ?? Faulted<bool>(new NullReferenceException());
    }
}
