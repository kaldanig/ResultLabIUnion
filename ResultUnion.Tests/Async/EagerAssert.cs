namespace ResultUnion.Tests.Async;

internal static class EagerAssert
{
    /// <summary>
    /// Asserts that <paramref name="call"/> throws <see cref="ArgumentNullException"/> synchronously, before any task exists.
    /// </summary>
    /// <remarks>
    /// Takes an <see cref="Action"/> on purpose: <c>Assert.ThrowsAsync</c> would also pass when the exception is stored in the
    /// returned task, which is exactly the lazy behaviour these tests rule out.
    /// </remarks>
    public static void ThrowsEagerly(Action call) => Assert.Throws<ArgumentNullException>(call);
}
