namespace ResultUnion.Tests;

/// <summary>
/// Results compare by what they expose: the case, the value of a success, the error of a failure. A default result is
/// a failure carrying <see cref="Error.DefaultResult"/>, so it equals one created with that error (DESIGN.md, 3).
/// </summary>
public sealed class ResultEqualityTests
{
    private static readonly Error NotFound = Error.NotFound("Missing.");

    [Fact]
    public void A_default_result_equals_a_failure_with_the_default_error()
    {
        AssertEqual(default(Result<int>), Result<int>.Failure(Error.DefaultResult));
        AssertEqual(default(Result<string>), Result<string>.Failure(Error.DefaultResult));
        AssertEqual(default(Result), Result.Failure(Error.DefaultResult));
        AssertEqual(default(Result<int>), default(Result<int>));

        // The operators the compiler generates call Equals.
        Assert.True(default(Result<int>) == Result<int>.Failure(Error.DefaultResult));
        Assert.False(default(Result<int>) != Result<int>.Failure(Error.DefaultResult));
        Assert.True(default(Result) == Result.Failure(Error.DefaultResult));
        Assert.False(default(Result) != Result.Failure(Error.DefaultResult));
    }

    [Fact]
    public void Successes_are_equal_when_their_values_are()
    {
        AssertEqual(Result<int>.Success(5), Result<int>.Success(5));
        AssertEqual(Result<string>.Success("ann"), Result<string>.Success(new string(['a', 'n', 'n'])));
        AssertEqual(Result.Success(), Result.Success());

        AssertNotEqual(Result<int>.Success(5), Result<int>.Success(6));
        AssertNotEqual(Result<string>.Success("ann"), Result<string>.Success("bob"));
    }

    [Fact]
    public void Failures_are_equal_when_their_errors_are()
    {
        AssertEqual(Result<int>.Failure(NotFound), Result<int>.Failure(Error.NotFound("Missing.")));
        AssertEqual(Result.Failure(NotFound), Result.Failure(Error.NotFound("Missing.")));

        AssertNotEqual(Result<int>.Failure(NotFound), Result<int>.Failure(Error.Conflict("Missing.")));
        AssertNotEqual(Result.Failure(NotFound), Result.Failure(Error.NotFound("Gone.")));
        AssertNotEqual(default(Result), Result.Failure(NotFound));
    }

    [Fact]
    public void A_success_never_equals_a_failure()
    {
        // A success of 0 and a default (failed) Result<int> hold the same value field; the case tells them apart.
        AssertNotEqual(Result<int>.Success(0), default(Result<int>));
        AssertNotEqual(Result<int>.Success(0), Result<int>.Failure(NotFound));
        AssertNotEqual(Result.Success(), default(Result));
        AssertNotEqual(Result.Success(), Result.Failure(NotFound));
    }

    [Fact]
    public void Results_work_as_set_members_and_dictionary_keys()
    {
        var seen = new HashSet<Result<int>> { default, Result<int>.Success(1), Result<int>.Failure(NotFound) };

        Assert.Contains(Result<int>.Failure(Error.DefaultResult), seen);
        Assert.Contains(Result<int>.Success(1), seen);
        Assert.Contains(Result<int>.Failure(Error.NotFound("Missing.")), seen);
        Assert.False(seen.Add(Result<int>.Failure(Error.DefaultResult)));
        Assert.Equal(3, seen.Count);
    }

    private static void AssertEqual<T>(T left, T right) where T : struct, IEquatable<T>
    {
        Assert.True(left.Equals(right));
        Assert.True(right.Equals(left));
        Assert.True(left.Equals((object)right));
        Assert.True(EqualityComparer<T>.Default.Equals(left, right));
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
    }

    private static void AssertNotEqual<T>(T left, T right) where T : struct, IEquatable<T>
    {
        Assert.False(left.Equals(right));
        Assert.False(right.Equals(left));
        Assert.False(left.Equals((object)right));
    }
}
