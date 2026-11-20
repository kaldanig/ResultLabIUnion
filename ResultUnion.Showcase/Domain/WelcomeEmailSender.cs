namespace ResultUnion.Showcase.Domain;

/// <summary>A fake email gateway: an asynchronous side effect that does not change the result.</summary>
public sealed class WelcomeEmailSender
{
    public async Task SendAsync(Email to, CancellationToken cancellationToken = default)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken);

        Console.WriteLine($"    [mail] welcome email sent to {to}");
    }
}
