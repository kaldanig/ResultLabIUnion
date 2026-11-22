#pragma warning disable RU0010 // these tests serialize results on purpose: that the library refuses is what they check

using System.Text.Json;

namespace ResultUnion.Tests.Serialization;

/// <summary>
/// A result is not a JSON contract: serializing or deserializing one throws, with a message that says what to do
/// instead, rather than writing the union's case (which sends a failure as a success). An error stays serializable.
/// </summary>
public sealed class ResultJsonConverterTests
{
    private static readonly Error NotFound = Error.NotFound("Missing.");

    [Fact]
    public void Writing_any_result_throws_and_says_what_to_do()
    {
        var success = Assert.Throws<NotSupportedException>(() => JsonSerializer.Serialize(Result<int>.Success(1)));
        var failure = Assert.Throws<NotSupportedException>(() => JsonSerializer.Serialize(Result<int>.Failure(NotFound)));
        var plain = Assert.Throws<NotSupportedException>(() => JsonSerializer.Serialize(Result.Success()));

        Assert.StartsWith("A Result<Int32> is not written as JSON: map it at the boundary first, with ToHttpResult", success.Message);
        Assert.StartsWith("A Result<Int32> is not written as JSON", failure.Message);
        Assert.StartsWith("A Result is not written as JSON", plain.Message);
    }

    [Fact]
    public void A_result_inside_another_object_or_as_object_throws_too()
    {
        Assert.Throws<NotSupportedException>(() => JsonSerializer.Serialize(new { result = Result<string>.Success("a") }));
        Assert.Throws<NotSupportedException>(() => JsonSerializer.Serialize<object>(Result.Failure(NotFound)));
        var nested = Assert.Throws<NotSupportedException>(() => JsonSerializer.Serialize(Result<List<int>>.Success([1])));

        Assert.StartsWith("A Result<List<Int32>> is not written as JSON", nested.Message);
    }

    [Fact]
    public void Reading_a_result_throws_and_points_to_your_own_type()
    {
        var generic = Assert.Throws<NotSupportedException>(() => JsonSerializer.Deserialize<Result<int>>("1"));
        var plain = Assert.Throws<NotSupportedException>(() => JsonSerializer.Deserialize<Result>("{}"));

        Assert.StartsWith("A Result<Int32> is not read from JSON: read a type of your own", generic.Message);
        Assert.StartsWith("A Result is not read from JSON", plain.Message);
    }

    [Fact]
    public void An_error_is_still_serializable()
    {
        var json = JsonSerializer.Serialize(new { reason = NotFound });

        Assert.Contains("\"code\":\"resource.not_found\"", json, StringComparison.Ordinal);
    }
}
