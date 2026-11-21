using System.Text.Json;

namespace ResultUnion.Showcase.Demos;

/// <summary>
/// An error in a message between services: System.Text.Json writes and reads it with no setup, because
/// <see cref="Error"/> carries its own converter. The receiver gets the same code, type, field and arguments.
/// </summary>
internal static class SerializationDemo
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    private sealed record RegistrationRejected(string Email, Error Reason);

    public static void Run()
    {
        var service = new RegistrationService(new UserRepository());

        // The sender publishes an event with the error (for example to a queue).
#pragma warning disable RU0004 // the demo input is known to fail
        var rejected = new RegistrationRejected("bob@example.com", service.Register("bob@example.com", "15").Error);
#pragma warning restore RU0004
        var json = JsonSerializer.Serialize(rejected, Indented);

        Console.WriteLine("  The event on the wire:");
        foreach (var line in json.Split('\n'))
        {
            Console.WriteLine($"    {line.TrimEnd()}");
        }

        // The receiver reads it back and decides on the type and code, as it would in-process.
        var received = JsonSerializer.Deserialize<RegistrationRejected>(json)!;
        Demo.Show("  received equals sent", received.Reason == rejected.Reason);
        Demo.Show("  code / type", $"{received.Reason.Code} / {received.Reason.Type}");
        Demo.Show("  minimumAge argument", received.Reason.Arguments["minimumAge"]);
        Demo.Show("  retry?", received.Reason.IsTransient);

        // A validation group keeps its details, with their fields, through the round trip.
#pragma warning disable RU0004 // the demo inputs are known to fail
        var invalid = Result.CombineAll(Email.Create("dan@"), Password.Create("123")).Error;
#pragma warning restore RU0004
        var copy = JsonSerializer.Deserialize<Error>(JsonSerializer.Serialize(invalid))!;
        Demo.Show("  group round trip", string.Join(", ", copy.Flatten().Select(error => error.Code)));
    }
}
