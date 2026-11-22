using Grpc.Core;

namespace ResultUnion.Grpc;

/// <summary>Maps <see cref="ErrorType"/> to gRPC <see cref="StatusCode"/>, and back for clients.</summary>
public static class GrpcStatusCodeMapping
{
    /// <param name="type">The error category.</param>
    extension(ErrorType type)
    {
        /// <summary>Gets the gRPC status code for the category.</summary>
        /// <remarks>
        /// Validation → InvalidArgument, Unauthorized → Unauthenticated, Forbidden → PermissionDenied, NotFound → NotFound,
        /// Conflict and BusinessRule → FailedPrecondition, Concurrency → Aborted, TooManyRequests → ResourceExhausted,
        /// Unavailable → Unavailable, Unexpected (and any unknown value) → Internal.
        /// <para>
        /// Conflict maps to FailedPrecondition ("the state prevents the operation; do not retry until it changes") rather than
        /// AlreadyExists, because a conflict is not always a duplicate. Concurrency maps to Aborted, which gRPC defines as
        /// "retry the whole operation", matching <see cref="ErrorTypeExtensions.get_IsTransient(ErrorType)">IsTransient</see>.
        /// </para>
        /// </remarks>
        public StatusCode ToGrpcStatusCode() => type switch
        {
            ErrorType.Validation => StatusCode.InvalidArgument,
            ErrorType.Unauthorized => StatusCode.Unauthenticated,
            ErrorType.Forbidden => StatusCode.PermissionDenied,
            ErrorType.NotFound => StatusCode.NotFound,
            ErrorType.Conflict or ErrorType.BusinessRule => StatusCode.FailedPrecondition,
            ErrorType.Concurrency => StatusCode.Aborted,
            ErrorType.TooManyRequests => StatusCode.ResourceExhausted,
            ErrorType.Unavailable => StatusCode.Unavailable,
            _ => StatusCode.Internal,
        };
    }

    /// <param name="code">The gRPC status code.</param>
    extension(StatusCode code)
    {
        /// <summary>Gets the error category for a gRPC status code, for errors received by a client.</summary>
        /// <remarks>
        /// The inverse of <see cref="ToGrpcStatusCode(ErrorType)"/> where it is one to one. FailedPrecondition becomes
        /// Conflict (the server's BusinessRule is not distinguishable by code alone; the error code tells), AlreadyExists
        /// becomes Conflict, DeadlineExceeded becomes Unavailable, and anything else, including OK, becomes Unexpected.
        /// </remarks>
        public ErrorType ToErrorType() => code switch
        {
            StatusCode.InvalidArgument or StatusCode.OutOfRange => ErrorType.Validation,
            StatusCode.Unauthenticated => ErrorType.Unauthorized,
            StatusCode.PermissionDenied => ErrorType.Forbidden,
            StatusCode.NotFound => ErrorType.NotFound,
            StatusCode.FailedPrecondition or StatusCode.AlreadyExists => ErrorType.Conflict,
            StatusCode.Aborted => ErrorType.Concurrency,
            StatusCode.ResourceExhausted => ErrorType.TooManyRequests,
            StatusCode.Unavailable or StatusCode.DeadlineExceeded => ErrorType.Unavailable,
            _ => ErrorType.Unexpected,
        };
    }
}
