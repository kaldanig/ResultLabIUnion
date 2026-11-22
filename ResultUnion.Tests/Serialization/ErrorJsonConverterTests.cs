using System.Text.Json;

namespace ResultUnion.Tests.Serialization;

public sealed class ErrorJsonConverterTests
{
    private static readonly Error TooYoung =
        Error.Create("user.too_young", "Ann is 15; the minimum age is 18.", ErrorType.BusinessRule)
            .WithArgument("minimumAge", 18)
            .WithArgument("age", 15);

    private static string Serialize(object value, JsonSerializerOptions? options = null) =>
        JsonSerializer.Serialize(value, options);

    private static Error Deserialize(string json, JsonSerializerOptions? options = null) =>
        JsonSerializer.Deserialize<Error>(json, options)!;

    // ---- format --------------------------------------------------------------------------------------------------

    [Fact]
    public void A_simple_error_has_code_message_and_type_only()
    {
        var json = Serialize(Error.NotFound("No such user."));

        Assert.Equal("""{"code":"resource.not_found","message":"No such user.","type":"NotFound"}""", json);
    }

    [Fact]
    public void Field_and_arguments_are_written_when_present()
    {
        var json = Serialize(TooYoung.WithField("age"));

        Assert.Equal(
            """{"code":"user.too_young","message":"Ann is 15; the minimum age is 18.","type":"BusinessRule","field":"age","arguments":{"minimumAge":18,"age":15}}""",
            json);
    }

    [Fact]
    public void Details_nest()
    {
        var group = Error.Validation([
            Error.Validation("Email is required.").WithField("email"),
            Error.Validation([Error.Validation("Quantity must be positive.").WithField("quantity")]).WithField("lines[0]"),
        ]);

        using var document = JsonDocument.Parse(Serialize(group));
        var details = document.RootElement.GetProperty("details");

        Assert.Equal(2, details.GetArrayLength());
        Assert.Equal("email", details[0].GetProperty("field").GetString());
        Assert.Equal("quantity", details[1].GetProperty("details")[0].GetProperty("field").GetString());
    }

    [Fact]
    public void Property_names_do_not_follow_a_naming_policy()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseUpper };

        var json = Serialize(TooYoung, options);

        Assert.StartsWith("""{"code":""", json);
        Assert.Equal(TooYoung, Deserialize(json, options));
    }

    // ---- round trips -----------------------------------------------------------------------------------------------

    [Fact]
    public void A_round_trip_keeps_every_part_of_the_error()
    {
        var original = Error.Validation([
            TooYoung.WithField("age"),
            Error.Create("email.invalid_format", "Not an email.", ErrorType.Validation)
                .WithField("email")
                .WithArgument("value", "bob@"),
        ]).WithArgument("count", 2);

        var copy = Deserialize(Serialize(original));

        Assert.Equal(original, copy);
        Assert.Equal("validation.failed", copy.Code);
        Assert.Equal(2, copy.Details.Count);
        Assert.Equal("age", copy.Details[0].Field);
        Assert.Equal(18, copy.Details[0].Arguments["minimumAge"]);
    }

    [Theory]
    [MemberData(nameof(EveryErrorType))]
    public void Every_error_type_round_trips(ErrorType type)
    {
        var original = Error.Create("some.code", "Some message.", type);

        Assert.Equal(original, Deserialize(Serialize(original)));
    }

    public static TheoryData<ErrorType> EveryErrorType() => [.. Enum.GetValues<ErrorType>()];

    [Fact]
    public void An_unexpected_error_keeps_its_message()
    {
        var original = Error.Unexpected("Connection string is missing.");

        Assert.Equal(original, Deserialize(Serialize(original)));
    }

    [Fact]
    public void Errors_inside_other_types_round_trip()
    {
        var message = new OrderRejected(Guid.NewGuid(), TooYoung, [Error.Conflict("Taken.")]);

        var copy = JsonSerializer.Deserialize<OrderRejected>(Serialize(message))!;

        Assert.Equal(message.OrderId, copy.OrderId);
        Assert.Equal(message.Reason, copy.Reason);
        Assert.Equal(message.Others, copy.Others);
    }

    private sealed record OrderRejected(Guid OrderId, Error Reason, IReadOnlyList<Error> Others);

    // ---- argument values ---------------------------------------------------------------------------------------

    [Fact]
    public void Arguments_come_back_as_json_values()
    {
        var id = Guid.NewGuid();
        var original = Error.Validation("x")
            .WithArgument("int", 42)
            .WithArgument("long", 5_000_000_000L)
            .WithArgument("decimal", 9.95m)
            .WithArgument("text", "abc")
            .WithArgument("flag", true)
            .WithArgument("none", null)
            .WithArgument("id", id)
            .WithArgument("type", ErrorType.Conflict)
            .WithArgument("list", new[] { 1, 2 });

        var arguments = Deserialize(Serialize(original)).Arguments;

        Assert.Equal(42, arguments["int"]);
        Assert.Equal(5_000_000_000L, arguments["long"]);
        Assert.Equal(9.95m, arguments["decimal"]);
        Assert.Equal("abc", arguments["text"]);
        Assert.Equal(true, arguments["flag"]);
        Assert.Null(arguments["none"]);
        Assert.Equal(id.ToString(), arguments["id"]);              // a Guid comes back as its string
        Assert.Equal("Conflict", arguments["type"]);               // an enum as its name
        var list = Assert.IsType<JsonElement>(arguments["list"]);  // an array stays JSON
        Assert.Equal("[1,2]", list.GetRawText());
    }

    [Fact]
    public void Every_kind_of_argument_value_is_written_as_json()
    {
        var original = Error.Validation("x")
            .WithArgument("short", (short)-3)
            .WithArgument("byte", (byte)7)
            .WithArgument("uint", 4_000_000_000u)
            .WithArgument("ulong", 18_000_000_000_000_000_000ul)
            .WithArgument("double", 0.5d)
            .WithArgument("float", 0.25f)
            .WithArgument("dateTime", new DateTime(2026, 11, 22, 10, 30, 0, DateTimeKind.Utc))
            .WithArgument("dateTimeOffset", new DateTimeOffset(2026, 11, 22, 10, 30, 0, TimeSpan.FromHours(4)))
            .WithArgument("dateOnly", new DateOnly(2026, 11, 22))
            .WithArgument("timeSpan", TimeSpan.FromMinutes(90))
            .WithArgument("element", JsonDocument.Parse("""{"a":[1,true]}""").RootElement)
            .WithArgument("record", new Limit("age", 18));

        using var json = JsonDocument.Parse(Serialize(original));
        var arguments = json.RootElement.GetProperty("arguments");

        Assert.Equal("-3", arguments.GetProperty("short").GetRawText());
        Assert.Equal("7", arguments.GetProperty("byte").GetRawText());
        Assert.Equal("4000000000", arguments.GetProperty("uint").GetRawText());
        Assert.Equal("18000000000000000000", arguments.GetProperty("ulong").GetRawText());
        Assert.Equal("0.5", arguments.GetProperty("double").GetRawText());
        Assert.Equal("0.25", arguments.GetProperty("float").GetRawText());
        Assert.Equal("\"2026-11-22T10:30:00Z\"", arguments.GetProperty("dateTime").GetRawText());
        Assert.Equal("\"2026-11-22T10:30:00+04:00\"", arguments.GetProperty("dateTimeOffset").GetRawText());
        Assert.Equal("\"2026-11-22\"", arguments.GetProperty("dateOnly").GetRawText());
        Assert.Equal("\"01:30:00\"", arguments.GetProperty("timeSpan").GetRawText());
        Assert.Equal("""{"a":[1,true]}""", arguments.GetProperty("element").GetRawText());
        Assert.Equal("""{"Name":"age","Value":18}""", arguments.GetProperty("record").GetRawText()); // other types: JsonSerializer
    }

    private sealed record Limit(string Name, int Value);

    [Fact]
    public void Retry_after_seconds_stays_a_number()
    {
        var original = Error.TooManyRequests("Slow down.").WithArgument("retryAfterSeconds", 30);

        Assert.Equal(30, Deserialize(Serialize(original)).Arguments["retryAfterSeconds"]);
    }

    // ---- reading ---------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("\"BusinessRule\"")]
    [InlineData("\"businessrule\"")]
    [InlineData("7")]
    public void The_type_is_read_as_a_name_or_a_number(string type)
    {
        var number = (int)ErrorType.BusinessRule;
        var json = $$"""{"code":"a.b","message":"m","type":{{(type == "7" ? number.ToString() : type)}}}""";

        Assert.Equal(ErrorType.BusinessRule, Deserialize(json).Type);
    }

    [Fact]
    public void Unknown_properties_and_null_optional_ones_are_ignored()
    {
        var error = Deserialize("""
            {"code":"a.b","message":"m","type":"Conflict","traceId":"x","extra":{"nested":[1,2]},
             "field":null,"arguments":null,"details":null}
            """);

        Assert.Equal(Error.Create("a.b", "m", ErrorType.Conflict), error);
    }

    [Theory]
    [InlineData("""[]""")]
    [InlineData("""{"message":"m","type":"Conflict"}""")]
    [InlineData("""{"code":" ","message":"m","type":"Conflict"}""")]
    [InlineData("""{"code":"a.b","type":"Conflict"}""")]
    [InlineData("""{"code":"a.b","message":"m"}""")]
    [InlineData("""{"code":"a.b","message":"m","type":"Sometimes"}""")]
    [InlineData("""{"code":"a.b","message":"m","type":"1"}""")]
    [InlineData("""{"code":"a.b","message":"m","type":999}""")]
    [InlineData("""{"code":1,"message":"m","type":"Conflict"}""")]
    [InlineData("""{"code":"a.b","message":"m","type":"Conflict","field":""}""")]
    [InlineData("""{"code":"a.b","message":"m","type":"Conflict","field":1}""")]
    [InlineData("""{"code":"a.b","message":"m","type":"Conflict","arguments":[]}""")]
    [InlineData("""{"code":"a.b","message":"m","type":"Conflict","details":{}}""")]
    [InlineData("""{"code":"a.b","message":"m","type":"Conflict","details":[{"code":"c"}]}""")]
    public void Invalid_json_throws_JsonException(string json)
    {
        Assert.Throws<JsonException>(() => Deserialize(json));
    }

    [Fact]
    public void A_null_error_reads_and_writes_as_null()
    {
        Assert.Null(JsonSerializer.Deserialize<Error>("null"));
        Assert.Equal("null", JsonSerializer.Serialize<Error?>(null));
    }

    [Fact]
    public void The_converter_called_directly_rejects_null_arguments()
    {
        // System.Text.Json writes null itself and never passes it to the converter; a direct caller might.
        var converter = new ResultUnion.Serialization.ErrorJsonConverter();
        using var writer = new Utf8JsonWriter(new MemoryStream());

        Assert.Throws<ArgumentNullException>(() => converter.Write(writer, null!, JsonSerializerOptions.Default));
        Assert.Throws<ArgumentNullException>(() => converter.Write(null!, TooYoung, JsonSerializerOptions.Default));
    }
}
