namespace ResultUnion.Showcase.Demos;

/// <summary>Reading the value or the error without pattern matching, and what happens when it is done wrongly.</summary>
internal static class SafeAccessDemo
{
    public static void Run()
    {
        var success = User.ParseAge("30");
        var failure = User.ParseAge("abc");

        // TryGetResultValue / TryGetError: the non-throwing, non-boxing way to read either track.
        if (success.TryGetResultValue(out var age))
        {
            Demo.Show("success.TryGetResultValue(out var age)", age);
        }

        Demo.Show("failure.TryGetResultValue(out _)", failure.TryGetResultValue(out _));

        if (failure.TryGetError(out var error))
        {
            Demo.Show("failure.TryGetError(out var error)", error);
        }

        // GetValueOrDefault: a fallback instead of an exception.
        Demo.Show("success.GetValueOrDefault(-1)", success.GetValueOrDefault(-1));
        Demo.Show("failure.GetValueOrDefault(-1)", failure.GetValueOrDefault(-1));
        Demo.Show("failure.GetValueOrDefault()  // careful: 0", failure.GetValueOrDefault());

        // ToResult: drop the value, keep the outcome.
        Demo.Show("success.ToResult()", success.ToResult());

        // ResultValue and Error throw when read on the wrong track: check the state first, or use Match/switch.
#pragma warning disable RU0004 // this section shows what reading each case does, the wrong one included
        Demo.Show("success.ResultValue", success.ResultValue);
        Demo.Show("failure.ResultValue", Capture(() => failure.ResultValue));
        Demo.Show("success.Error", Capture(() => success.Error));
#pragma warning restore RU0004

        // Guard clauses: delegates are validated eagerly, even on the track where they would not run.
        Demo.Show("failure.Map<int>(null!)", Capture(() => failure.Map<int>(null!)));
    }

    private static string Capture(Func<object> read)
    {
        try
        {
            return $"{read()}";
        }
        catch (Exception exception)
        {
            return $"error {exception.GetType().Name}: {exception.Message}";
        }
    }
}
