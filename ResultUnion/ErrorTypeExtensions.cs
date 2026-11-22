namespace ResultUnion;

/// <summary>Retry semantics of <see cref="ErrorType"/> and <see cref="Error"/>.</summary>
public static class ErrorTypeExtensions
{
    /// <param name="type">The error category.</param>
    extension(ErrorType type)
    {
        /// <summary>Gets a value indicating whether running the operation again may succeed without any change to its input.</summary>
        /// <remarks>
        /// <para>
        /// <see langword="true"/> for <see cref="ErrorType.Unavailable"/> and <see cref="ErrorType.TooManyRequests"/> (retry after a wait),
        /// and for <see cref="ErrorType.Concurrency"/> (retry the whole operation, so it reads fresh data first; resending the same
        /// stale update fails again). <see langword="false"/> for every other category, including unknown values.
        /// </para>
        /// <para>Use it in retry policies, outbox processors and background jobs: <c>if (error.IsTransient) { … }</c>.</para>
        /// <para>A property, like <see cref="get_IsTransient(Error)">Error.IsTransient</see>; until 0.4.0 it was a method.</para>
        /// </remarks>
        public bool IsTransient => type is ErrorType.Unavailable or ErrorType.TooManyRequests or ErrorType.Concurrency;
    }

    /// <param name="error">The error to inspect.</param>
    extension(Error error)
    {
        /// <summary>Gets a value indicating whether running the operation again may succeed: <see cref="get_IsTransient(ErrorType)">IsTransient</see> of <see cref="Error.Type"/>.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="error"/> is <see langword="null"/>.</exception>
        public bool IsTransient => (error ?? throw new ArgumentNullException(nameof(error))).Type.IsTransient;
    }
}
