namespace ResultUnion.Shop.Web;

/// <summary>
/// An error as a form shows it: the messages of each invalid field next to its input, and the error's own message and
/// code at the top. The services already wrote the messages in the user's language; the code is what code switches on.
/// </summary>
public sealed class ErrorView
{
    private readonly ILookup<string, string> _byField;

    /// <param name="error">The error, with the messages the services wrote in <paramref name="language"/>.</param>
    /// <param name="language">The language the user picked (<c>en</c> or <c>ka</c>), for the hint the app writes itself.</param>
    public ErrorView(Error error, string language)
    {
        Code = error.Code;
        Message = error.Message;
        Transient = error.IsTransient;
        Hint = (error.Type, language) switch
        {
            (ErrorType.Concurrency, "ka") => "ეს სხვამ შეცვალა მას შემდეგ, რაც წაიკითხეთ: განაახლეთ და ხელახლა სცადეთ.",
            (ErrorType.Concurrency, _) => "Someone changed it since you read it: reload, then try again.",
            (ErrorType.Unavailable or ErrorType.TooManyRequests, "ka") => "ცოტა ხანში სცადეთ.",
            (ErrorType.Unavailable or ErrorType.TooManyRequests, _) => "Try again in a moment.",
            _ => null,
        };
        AllOnFields = error.Flatten().All(entry => entry.Field is not null);
        _byField = error.Flatten()
            .Where(entry => entry.Field is not null)
            .ToLookup(entry => entry.Field!, entry => entry.Message, StringComparer.Ordinal);
    }

    public string Code { get; }

    public string Message { get; }

    /// <summary>
    /// Every message belongs to a field (one invalid input, or a <c>validation.failed</c> group), so the form shows them
    /// next to the inputs and no line of its own. ResultUnion.Http rebuilds a group with its generic English message,
    /// so that line would not follow the chosen language anyway; the field messages do.
    /// </summary>
    public bool AllOnFields { get; }

    /// <summary>Worth trying again: the service is down or busy, or the data changed since it was read.</summary>
    public bool Transient { get; }

    /// <summary>What the user can do about a transient error; the category says which, not the code.</summary>
    public string? Hint { get; }

    public IEnumerable<string> For(string field) => _byField[field];

    public bool Has(string field) => _byField.Contains(field);

    /// <summary>The field errors that no input on the form shows, so that none is lost.</summary>
    public IEnumerable<string> Others(params string[] shown) =>
        _byField.Where(group => !shown.Contains(group.Key)).SelectMany(group => group.Select(message => $"{group.Key}: {message}"));
}
