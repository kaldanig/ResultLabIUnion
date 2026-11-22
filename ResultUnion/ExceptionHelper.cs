using System.Diagnostics.CodeAnalysis;

namespace ResultUnion;

internal static class ExceptionHelper
{
    [DoesNotReturn]
    internal static T ResultValueOnFailure<T>() =>
        throw new InvalidOperationException("Cannot access 'ResultValue' when the 'Result' is a failure.");

    [DoesNotReturn]
    internal static Error ErrorOnSuccess() =>
        throw new InvalidOperationException("Cannot access 'Error' when the 'Result' is a success.");
}