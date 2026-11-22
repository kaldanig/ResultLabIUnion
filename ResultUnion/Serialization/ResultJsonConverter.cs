using System.Text.Json;
using System.Text.Json.Serialization;

namespace ResultUnion.Serialization;

/// <summary>
/// Refuses to write or read a <see cref="Result{T}"/> as JSON, with a message that says what to do instead.
/// </summary>
/// <remarks>
/// A result is not a JSON contract (DESIGN.md, 42). Without this converter, System.Text.Json in .NET 11 treats
/// <see cref="Result{T}"/> as a union and writes its case: a failure returned from a Minimal API endpoint goes out as
/// <c>200 OK</c> with the error as the body, so the client takes it for a success. The open generic converter in the
/// attribute needs .NET 11 and keeps <c>MakeGenericType</c> out of the AOT path. Analyzer RU0010 reports the same
/// mistakes at build time; this is for the ones it cannot see (a result passed on as <see cref="object"/>).
/// </remarks>
internal sealed class ResultJsonConverter<T> : JsonConverter<Result<T>>
    where T : notnull
{
    public override Result<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        throw new NotSupportedException(ResultJson.CannotRead(typeof(Result<T>)));

    public override void Write(Utf8JsonWriter writer, Result<T> value, JsonSerializerOptions options) =>
        throw new NotSupportedException(ResultJson.CannotWrite(typeof(Result<T>)));
}

/// <summary>The same for <see cref="Result"/>, which System.Text.Json would otherwise write property by property.</summary>
internal sealed class ResultJsonConverter : JsonConverter<Result>
{
    public override Result Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        throw new NotSupportedException(ResultJson.CannotRead(typeof(Result)));

    public override void Write(Utf8JsonWriter writer, Result value, JsonSerializerOptions options) =>
        throw new NotSupportedException(ResultJson.CannotWrite(typeof(Result)));
}

internal static class ResultJson
{
    public static string CannotWrite(Type type) =>
        $"A {Name(type)} is not written as JSON: map it at the boundary first, with ToHttpResult for an HTTP response, "
        + "or Match / a switch into a type of your own. An Error can be serialized on its own.";

    public static string CannotRead(Type type) =>
        $"A {Name(type)} is not read from JSON: read a type of your own, and turn it into a result "
        + "(or use ReadResultAsync from ResultUnion.Http for an HTTP response).";

    // Result<Dictionary<string, int>> rather than Result`1.
    private static string Name(Type type)
    {
        if (!type.IsGenericType)
            return type.Name;

        var name = type.Name[..type.Name.IndexOf('`', StringComparison.Ordinal)];
        return $"{name}<{string.Join(", ", type.GetGenericArguments().Select(Name))}>";
    }
}
