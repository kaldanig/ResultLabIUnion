namespace ResultUnion.Showcase.Demos;

/// <summary>Result.Try / Result.TryAsync: the boundary with APIs that report failures by throwing.</summary>
internal static class TryDemo
{
    public static async Task RunAsync()
    {
        // Sync: a parser that throws on bad input.
        foreach (var input in new[] { "https://example.com/users", "not a url" })
        {
            var uri = Result.Try(
                () => new Uri(input, UriKind.Absolute),
                exception => Error.Validation($"'{input}' is not a valid URL ({exception.GetType().Name})."));

            Demo.Show($"Try(new Uri(\"{input}\")).Map(Host)", uri.Map(u => u.Host));
        }

        // Sync, without a value: a file system call.
        var missingPath = Path.Combine(Path.GetTempPath(), $"result-union-{Guid.NewGuid():N}", "settings.json");
        Demo.Show("Try(() => File.Delete(<missing dir>))", Result.Try(
            () => File.Delete(missingPath),
            exception => Error.Unavailable($"Storage error: {exception.GetType().Name}.")));

        // Async: the exception is thrown after the first await and is still observed.
        var settings = await Result.TryAsync(
                () => File.ReadAllTextAsync(missingPath),
                exception => Error.NotFound($"Settings file is missing ({exception.GetType().Name})."))
            .MapAsync(text => text.Length);
        Demo.Show("TryAsync(() => File.ReadAllTextAsync(...))", settings);

        // Try is just the entry point: the pipeline continues as usual.
        var port = await Result.TryAsync(
                async () => { await Task.Yield(); return int.Parse("80800"); },
                exception => Error.Validation(exception.Message))
            .EnsureAsync(p => p is > 0 and <= 65535, p => Error.Validation($"Port {p} is out of range."))
            .MatchAsync(p => $"listening on {p}", error => $"error {error.Code}: {error.Message}");
        Demo.Show("TryAsync(parse).EnsureAsync(range)", port);

        // Cancellation is not caught: it stays an exception.
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        try
        {
            await Result.TryAsync(
                () => Task.Delay(1000, cancellation.Token),
                exception => Error.Unexpected(exception.Message));
        }
        catch (OperationCanceledException exception)
        {
            Demo.Show("TryAsync(..., cancelled token)", $"error {exception.GetType().Name} (not turned into a Result)");
        }

        // Note: never pass an async lambda to Result.Try; exceptions after its first await would not be observed. Use Result.TryAsync.
    }
}
