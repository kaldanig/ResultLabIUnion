namespace ResultUnion.Showcase;

/// <summary>Output helpers for the showcase.</summary>
internal static class Demo
{
    public static void Section(string title, Action body)
    {
        WriteTitle(title);
        body();
    }

    public static async Task SectionAsync(string title, Func<Task> body)
    {
        WriteTitle(title);
        await body();
    }

    private static void WriteTitle(string title)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"=== {title} ===");
        Console.ResetColor();
    }

    public static void Show(string label, object? value)
    {
        Console.Write($"  {label,-44} ");

        Console.ForegroundColor = value?.ToString() switch
        {
            { } s when s.StartsWith("Failure", StringComparison.Ordinal) || s.StartsWith("error", StringComparison.Ordinal) => ConsoleColor.Yellow,
            _ => ConsoleColor.Green,
        };
        Console.WriteLine(value);
        Console.ResetColor();
    }
}
