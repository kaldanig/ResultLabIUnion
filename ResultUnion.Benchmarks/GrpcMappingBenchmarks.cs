using BenchmarkDotNet.Attributes;
using Grpc.Core;
using ResultUnion.Grpc;

namespace ResultUnion.Benchmarks;

/// <summary>
/// What ResultUnion.Grpc adds to a failed call (docs/BENCHMARKS.md, "gRPC errors"): the service turns the error into a
/// rich status (google.rpc.Status with ErrorInfo and, for validation, BadRequest, serialized into the trailers), and a
/// client reads it back into an error. A successful call goes through none of this.
/// </summary>
public class GrpcMappingBenchmarks
{
    [Params("not found", "two invalid fields")]
    public string Failure { get; set; } = "not found";

    private Error _error = null!;
    private RpcException _received = null!;

    [GlobalSetup]
    public void Setup()
    {
        _error = Failure == "not found"
            ? Error.Create("user.not_found", "No user with the email ann@example.com.", ErrorType.NotFound).WithArgument("email", "ann@example.com")
            : Error.Validation(
            [
                Error.Create("email.invalid_format", "'ann@' is not a valid email.", ErrorType.Validation).WithArgument("value", "ann@").WithField("email"),
                Error.Create("password.too_short", "Password must be at least 8 characters.", ErrorType.Validation).WithArgument("minimumLength", 8).WithField("password"),
            ]);
        _received = _error.ToRpcException();
    }

    [Benchmark(Baseline = true, Description = "service: Error to google.rpc.Status")]
    public int ToStatus() => _error.ToRpcStatus().Details.Count;

    [Benchmark(Description = "service: Error to RpcException (status in the trailers)")]
    public int ToException() => _error.ToRpcException().Trailers.Count;

    [Benchmark(Description = "client: RpcException to Error")]
    public int ToError() => _received.ToError().Code.Length;
}
