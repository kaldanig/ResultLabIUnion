using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ResultUnion.Serialization;

/// <summary>
/// Reads and writes an <see cref="Error"/> as JSON, for messages and events between services. Applied to
/// <see cref="Error"/> through <see cref="JsonConverterAttribute"/>, so it needs no registration.
/// </summary>
/// <remarks>
/// <para>The format, with fixed property names (a naming policy does not change them, because they are a contract):</para>
/// <code language="json">
/// {
///   "code": "user.too_young",
///   "message": "Ann is 15; the minimum age is 18.",
///   "type": "BusinessRule",
///   "field": "age",
///   "arguments": { "minimumAge": 18, "age": 15 },
///   "details": [ ]
/// }
/// </code>
/// <para>
/// <c>type</c> is written as the <see cref="ErrorType"/> name and read as a name (ignoring case) or a number of a
/// defined value. <c>field</c>, <c>arguments</c> and <c>details</c> are left out when empty; details nest. Unknown
/// properties are skipped, so a newer sender can add some.
/// </para>
/// <para>
/// Arguments come back as JSON values: a whole number as <see cref="int"/> (or <see cref="long"/>, or
/// <see cref="decimal"/> when larger), a fractional number as <see cref="decimal"/> (or <see cref="double"/> outside
/// its range), a string as <see cref="string"/>, <c>true</c>/<c>false</c> as <see cref="bool"/>, <c>null</c> as
/// <see langword="null"/>, and an object or array as a <see cref="JsonElement"/>. So an argument written as a
/// <see cref="Guid"/>, a date or an enum comes back as its string; keep arguments to numbers and strings when an error
/// must compare equal after a round trip.
/// </para>
/// <para>
/// The message of an <see cref="ErrorType.Unexpected"/> error is written as it is. Unlike the HTTP and gRPC mappings,
/// this is meant for trusted services, not for clients.
/// </para>
/// </remarks>
public sealed class ErrorJsonConverter : JsonConverter<Error>
{
    private static readonly JsonEncodedText CodeName = JsonEncodedText.Encode("code");
    private static readonly JsonEncodedText MessageName = JsonEncodedText.Encode("message");
    private static readonly JsonEncodedText TypeName = JsonEncodedText.Encode("type");
    private static readonly JsonEncodedText FieldName = JsonEncodedText.Encode("field");
    private static readonly JsonEncodedText ArgumentsName = JsonEncodedText.Encode("arguments");
    private static readonly JsonEncodedText DetailsName = JsonEncodedText.Encode("details");

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, Error value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);

        writer.WriteStartObject();
        writer.WriteString(CodeName, value.Code);
        writer.WriteString(MessageName, value.Message);
        writer.WriteString(TypeName, value.Type.ToString());

        if (value.Field is not null)
        {
            writer.WriteString(FieldName, value.Field);
        }

        if (value.Arguments.Count > 0)
        {
            writer.WriteStartObject(ArgumentsName);
            foreach (var (name, argument) in value.Arguments)
            {
                writer.WritePropertyName(name);
                WriteArgument(writer, argument, options);
            }
            writer.WriteEndObject();
        }

        if (value.Details.Count > 0)
        {
            writer.WriteStartArray(DetailsName);
            foreach (var detail in value.Details)
            {
                Write(writer, detail, options);
            }
            writer.WriteEndArray();
        }

        writer.WriteEndObject();
    }

    /// <inheritdoc />
    /// <exception cref="JsonException">The JSON is not an error object: not an object, a missing or empty code or
    /// message, a missing or unknown type, or a malformed field, arguments or details.</exception>
    public override Error Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException($"An error must be a JSON object, not {reader.TokenType}.");
        }

        string? code = null;
        string? message = null;
        ErrorType? type = null;
        string? field = null;
        Dictionary<string, object?>? arguments = null;
        List<Error>? details = null;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return Create(code, message, type, field, details, arguments);
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException($"Unexpected {reader.TokenType} in an error.");
            }

            if (reader.ValueTextEquals(CodeName.EncodedUtf8Bytes))
            {
                code = ReadString(ref reader, "code");
            }
            else if (reader.ValueTextEquals(MessageName.EncodedUtf8Bytes))
            {
                message = ReadString(ref reader, "message");
            }
            else if (reader.ValueTextEquals(TypeName.EncodedUtf8Bytes))
            {
                type = ReadType(ref reader);
            }
            else if (reader.ValueTextEquals(FieldName.EncodedUtf8Bytes))
            {
                reader.Read();
                field = reader.TokenType == JsonTokenType.Null ? null : ReadStringValue(ref reader, "field");
            }
            else if (reader.ValueTextEquals(ArgumentsName.EncodedUtf8Bytes))
            {
                arguments = ReadArguments(ref reader);
            }
            else if (reader.ValueTextEquals(DetailsName.EncodedUtf8Bytes))
            {
                details = ReadDetails(ref reader, options);
            }
            else
            {
                reader.Read();
                reader.Skip();   // unknown property: forward compatible
            }
        }

        throw new JsonException("The JSON ends inside an error.");
    }

    // ---- reading -------------------------------------------------------------------------------------------------

    private static Error Create(
        string? code, string? message, ErrorType? type, string? field, List<Error>? details, Dictionary<string, object?>? arguments)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new JsonException("An error needs a non-empty \"code\".");
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            throw new JsonException("An error needs a non-empty \"message\".");
        }

        if (type is null)
        {
            throw new JsonException("An error needs a \"type\".");
        }

        if (field is not null && string.IsNullOrWhiteSpace(field))
        {
            throw new JsonException("An error's \"field\" must not be empty.");
        }

        return Error.Restore(code, message, type.Value, field, details, arguments);
    }

    private static string ReadString(ref Utf8JsonReader reader, string property)
    {
        reader.Read();
        return ReadStringValue(ref reader, property);
    }

    private static string ReadStringValue(ref Utf8JsonReader reader, string property) =>
        reader.TokenType == JsonTokenType.String
            ? reader.GetString()!
            : throw new JsonException($"An error's \"{property}\" must be a string, not {reader.TokenType}.");

    private static ErrorType ReadType(ref Utf8JsonReader reader)
    {
        reader.Read();

        switch (reader.TokenType)
        {
            case JsonTokenType.String:
            {
                var name = reader.GetString();
                if (Enum.TryParse<ErrorType>(name, ignoreCase: true, out var parsed)
                    && Enum.IsDefined(parsed)
                    && !int.TryParse(name, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                {
                    return parsed;
                }

                throw new JsonException($"Unknown error type \"{name}\".");
            }

            case JsonTokenType.Number when reader.TryGetInt32(out var number) && Enum.IsDefined((ErrorType)number):
                return (ErrorType)number;

            default:
                throw new JsonException($"An error's \"type\" must be an ErrorType name or number, not {reader.TokenType}.");
        }
    }

    private static Dictionary<string, object?>? ReadArguments(ref Utf8JsonReader reader)
    {
        reader.Read();

        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException($"An error's \"arguments\" must be an object, not {reader.TokenType}.");
        }

        var arguments = new Dictionary<string, object?>(StringComparer.Ordinal);

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            var name = reader.GetString()!;
            reader.Read();
            arguments[name] = ReadArgument(ref reader);
        }

        return arguments;
    }

    private static object? ReadArgument(ref Utf8JsonReader reader) => reader.TokenType switch
    {
        JsonTokenType.String => reader.GetString(),
        JsonTokenType.True => true,
        JsonTokenType.False => false,
        JsonTokenType.Null => null,
        JsonTokenType.Number => ReadNumber(ref reader),
        _ => JsonElement.ParseValue(ref reader),   // object or array, kept as JSON
    };

    private static object ReadNumber(ref Utf8JsonReader reader)
    {
        if (reader.TryGetInt32(out var int32))
        {
            return int32;
        }

        if (reader.TryGetInt64(out var int64))
        {
            return int64;
        }

        return reader.TryGetDecimal(out var number) ? number : reader.GetDouble();
    }

    private List<Error>? ReadDetails(ref Utf8JsonReader reader, JsonSerializerOptions options)
    {
        reader.Read();

        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException($"An error's \"details\" must be an array, not {reader.TokenType}.");
        }

        var details = new List<Error>();

        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            details.Add(Read(ref reader, typeof(Error), options));
        }

        return details;
    }

    // ---- writing -------------------------------------------------------------------------------------------------

    private static void WriteArgument(Utf8JsonWriter writer, object? value, JsonSerializerOptions options)
    {
        switch (value)
        {
            case null: writer.WriteNullValue(); break;
            case string text: writer.WriteStringValue(text); break;
            case bool flag: writer.WriteBooleanValue(flag); break;
            case int number: writer.WriteNumberValue(number); break;
            case long number: writer.WriteNumberValue(number); break;
            case short number: writer.WriteNumberValue(number); break;
            case byte number: writer.WriteNumberValue(number); break;
            case uint number: writer.WriteNumberValue(number); break;
            case ulong number: writer.WriteNumberValue(number); break;
            case decimal number: writer.WriteNumberValue(number); break;
            case double number: writer.WriteNumberValue(number); break;
            case float number: writer.WriteNumberValue(number); break;
            case Guid guid: writer.WriteStringValue(guid); break;
            case DateTime date: writer.WriteStringValue(date); break;
            case DateTimeOffset date: writer.WriteStringValue(date); break;
            case DateOnly date: writer.WriteStringValue(date.ToString("O", CultureInfo.InvariantCulture)); break;
            case TimeSpan time: writer.WriteStringValue(time.ToString("c", CultureInfo.InvariantCulture)); break;
            case Enum member: writer.WriteStringValue(member.ToString()); break;
            case JsonElement element: element.WriteTo(writer); break;
            default: JsonSerializer.Serialize(writer, value, value.GetType(), options); break;
        }
    }
}
