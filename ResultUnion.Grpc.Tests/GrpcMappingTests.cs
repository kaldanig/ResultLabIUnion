using Google.Rpc;
using Grpc.Core;
using static ResultUnion.Grpc.Tests.EagerAssert;

namespace ResultUnion.Grpc.Tests;

public class GrpcMappingTests
{
    private static readonly Error TooYoung =
        Error.Create("user.too_young", "Users must be at least 18; this one is 15.", ErrorType.BusinessRule)
            .WithArgument("minimumAge", 18)
            .WithArgument("age", 15);

    private static Error FormErrors() => Error.Validation(
    [
        Error.Create("email.invalid_format", "'dan@' is not a valid email.", ErrorType.Validation).WithField("email"),
        Error.Create("password.too_short", "Too short.", ErrorType.Validation).WithArgument("minimumLength", 8).WithField("password"),
    ]);

    // Status codes

    [Theory]
    [InlineData(ErrorType.Validation, StatusCode.InvalidArgument)]
    [InlineData(ErrorType.Unauthorized, StatusCode.Unauthenticated)]
    [InlineData(ErrorType.Forbidden, StatusCode.PermissionDenied)]
    [InlineData(ErrorType.NotFound, StatusCode.NotFound)]
    [InlineData(ErrorType.Conflict, StatusCode.FailedPrecondition)]
    [InlineData(ErrorType.BusinessRule, StatusCode.FailedPrecondition)]
    [InlineData(ErrorType.Concurrency, StatusCode.Aborted)]
    [InlineData(ErrorType.TooManyRequests, StatusCode.ResourceExhausted)]
    [InlineData(ErrorType.Unavailable, StatusCode.Unavailable)]
    [InlineData(ErrorType.Unexpected, StatusCode.Internal)]
    [InlineData((ErrorType)99, StatusCode.Internal)]
    public void ToGrpcStatusCode_MapsEveryErrorType(ErrorType type, StatusCode expected)
    {
        Assert.Equal(expected, type.ToGrpcStatusCode());
    }

    [Theory]
    [InlineData(StatusCode.OK, ErrorType.Unexpected)]
    [InlineData(StatusCode.Cancelled, ErrorType.Unexpected)]
    [InlineData(StatusCode.Unknown, ErrorType.Unexpected)]
    [InlineData(StatusCode.InvalidArgument, ErrorType.Validation)]
    [InlineData(StatusCode.DeadlineExceeded, ErrorType.Unavailable)]
    [InlineData(StatusCode.NotFound, ErrorType.NotFound)]
    [InlineData(StatusCode.AlreadyExists, ErrorType.Conflict)]
    [InlineData(StatusCode.PermissionDenied, ErrorType.Forbidden)]
    [InlineData(StatusCode.ResourceExhausted, ErrorType.TooManyRequests)]
    [InlineData(StatusCode.FailedPrecondition, ErrorType.Conflict)]
    [InlineData(StatusCode.Aborted, ErrorType.Concurrency)]
    [InlineData(StatusCode.OutOfRange, ErrorType.Validation)]
    [InlineData(StatusCode.Unimplemented, ErrorType.Unexpected)]
    [InlineData(StatusCode.Internal, ErrorType.Unexpected)]
    [InlineData(StatusCode.Unavailable, ErrorType.Unavailable)]
    [InlineData(StatusCode.DataLoss, ErrorType.Unexpected)]
    [InlineData(StatusCode.Unauthenticated, ErrorType.Unauthorized)]
    public void ToErrorType_MapsStatusCodesBack(StatusCode code, ErrorType expected)
    {
        Assert.Equal(expected, code.ToErrorType());
    }

    // google.rpc.Status

    [Fact]
    public void ToRpcStatus_HasCodeMessageAndErrorInfo()
    {
        var status = TooYoung.ToRpcStatus("users.example.com");

        Assert.Equal((int)StatusCode.FailedPrecondition, status.Code);
        Assert.Equal(TooYoung.Message, status.Message);

        var info = status.GetDetail<ErrorInfo>();
        Assert.Equal("USER_TOO_YOUNG", info.Reason);
        Assert.Equal("users.example.com", info.Domain);
        Assert.Equal("user.too_young", info.Metadata["code"]);
        Assert.Equal("BusinessRule", info.Metadata["type"]);
        Assert.Equal("18", info.Metadata["minimumAge"]);
        Assert.Null(status.GetDetail<BadRequest>());
    }

    [Fact]
    public void ToRpcStatus_WritesArgumentValuesAsInvariantText()
    {
        var error = Error.Conflict("Taken.")
            .WithArgument("price", 9.5m)
            .WithArgument("none", null)
            .WithArgument("email", "ann@example.com")
            .WithArgument("flag", true)
            .WithArgument("uri", new Uri("https://example.com/a"));

        var metadata = error.ToRpcStatus().GetDetail<ErrorInfo>().Metadata;

        Assert.Equal("9.5", metadata["price"]);   // not "9,5" in another culture
        Assert.Equal("", metadata["none"]);
        Assert.Equal("ann@example.com", metadata["email"]);
        Assert.Equal("True", metadata["flag"]);
        Assert.Equal("https://example.com/a", metadata["uri"]);
    }

    [Fact]
    public void ToRpcStatus_ListsEveryInvalidFieldInBadRequest()
    {
        var status = FormErrors().ToRpcStatus();

        Assert.Equal((int)StatusCode.InvalidArgument, status.Code);
        var violations = status.GetDetail<BadRequest>().FieldViolations;
        Assert.Equal(["email", "password"], violations.Select(v => v.Field));
        Assert.Equal(["EMAIL_INVALID_FORMAT", "PASSWORD_TOO_SHORT"], violations.Select(v => v.Reason));
        Assert.Equal("'dan@' is not a valid email.", violations[0].Description);
    }

    [Fact]
    public void ToRpcStatus_TooManyRequests_AddsRetryInfo()
    {
        var status = Error.TooManyRequests("Slow down.").WithArgument("retryAfterSeconds", 30).ToRpcStatus();

        Assert.Equal((int)StatusCode.ResourceExhausted, status.Code);
        Assert.Equal(TimeSpan.FromSeconds(30), status.GetDetail<RetryInfo>().RetryDelay.ToTimeSpan());
    }

    [Fact]
    public void ToRpcStatus_Unexpected_HidesMessageAndArguments()
    {
        var status = Error.Unexpected("NullReferenceException at row 7.").WithArgument("row", 7).ToRpcStatus();

        Assert.Equal((int)StatusCode.Internal, status.Code);
        Assert.Equal(RpcErrorMapping.UnexpectedMessage, status.Message);
        Assert.False(status.GetDetail<ErrorInfo>().Metadata.ContainsKey("row"));
    }

    [Fact]
    public void ToRpcException_CarriesTheStatusCode()
    {
        var exception = Error.NotFound("No user 7.").ToRpcException();

        Assert.Equal(StatusCode.NotFound, exception.StatusCode);
        Assert.Equal("No user 7.", exception.Status.Detail);
    }

    // Round trips: server Error -> RpcException -> client Error

    [Fact]
    public void RoundTrip_KeepsTypeCodeMessageAndArguments()
    {
        var restored = TooYoung.ToRpcException().ToError();

        Assert.Equal(ErrorType.BusinessRule, restored.Type);                 // not just FailedPrecondition -> Conflict
        Assert.Equal("user.too_young", restored.Code);
        Assert.Equal(TooYoung.Message, restored.Message);
        Assert.Equal("18", restored.Arguments["minimumAge"]);                // argument values arrive as strings
    }

    [Fact]
    public void RoundTrip_ErrorWithField_ComesBackWithTheField()
    {
        var restored = Error.NotFound("No user zoe@example.com.").WithField("customerEmail").ToRpcException().ToError();

        Assert.Equal("customerEmail", restored.Field);
        Assert.Equal(ErrorType.NotFound, restored.Type);
        Assert.Empty(restored.Details);
    }

    [Fact]
    public void RoundTrip_ValidationGroup_KeepsFieldsAndFieldCodes()
    {
        var restored = FormErrors().ToRpcException().ToError();

        Assert.Equal("validation.failed", restored.Code);
        Assert.Equal(["email", "password"], restored.Details.Select(d => d.Field));
        Assert.Equal(["email.invalid_format", "password.too_short"], restored.Details.Select(d => d.Code));
    }

    [Fact]
    public void RoundTrip_NestedCollectionPaths()
    {
        var lines = new[] { 0, 5, -1 }.CombineAll(
            q => q > 0 ? Result<int>.Success(q) : Error.Create("order_line.quantity_not_positive", $"Was {q}.", ErrorType.Validation).WithField("quantity"),
            field: "lines").Error;

        var restored = lines.ToRpcException().ToError();

        Assert.Equal(["lines[0].quantity", "lines[2].quantity"], restored.Details.Select(d => d.Field));
    }

    [Fact]
    public void ToError_PlainStatusFromAnyServer_UsesTheStatusCode()
    {
        var restored = new RpcException(new global::Grpc.Core.Status(StatusCode.DeadlineExceeded, "Deadline exceeded")).ToError();

        Assert.Equal("grpc.deadline_exceeded", restored.Code);
        Assert.Equal(ErrorType.Unavailable, restored.Type);
        Assert.True(restored.IsTransient);
    }

    // Service boundary helpers

    [Fact]
    public void GetValueOrThrowRpc_ReturnsTheValueOrThrows()
    {
        Assert.Equal(7, Result<int>.Success(7).GetValueOrThrowRpc());

        var exception = Assert.Throws<RpcException>(() => Result<int>.Failure(Error.NotFound("x")).GetValueOrThrowRpc());
        Assert.Equal(StatusCode.NotFound, exception.StatusCode);
    }

    [Fact]
    public void GetValueOrThrowRpc_WithoutValue_BuildsTheReply()
    {
        Assert.Equal("empty", Result.Success().GetValueOrThrowRpc(() => "empty"));
        Assert.Throws<RpcException>(() => Result.Failure(Error.Forbidden("x")).GetValueOrThrowRpc(() => "empty"));
    }

    [Fact]
    public async Task GetValueOrThrowRpcAsync_AwaitsFirst()
    {
        Assert.Equal(7, await Task.FromResult(Result<int>.Success(7)).GetValueOrThrowRpcAsync());
        Assert.Equal("empty", await Task.FromResult(Result.Success()).GetValueOrThrowRpcAsync(() => "empty"));

        var exception = await Assert.ThrowsAsync<RpcException>(() => Task.FromResult(Result<int>.Failure(TooYoung)).GetValueOrThrowRpcAsync());
        Assert.Equal(StatusCode.FailedPrecondition, exception.StatusCode);
    }

    [Fact]
    public void NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => ((Error)null!).ToRpcStatus());
        Assert.Throws<ArgumentNullException>(() => ((RpcException)null!).ToError());
        Assert.Throws<ArgumentNullException>(() => Result.Success().GetValueOrThrowRpc<string>(null!));
        ThrowsEagerly(() => _ = ((Task<Result<int>>)null!).GetValueOrThrowRpcAsync());
    }

    // Client side: an RpcException from a call becomes a Result again with the typed TryAsync.
    [Fact]
    public async Task Client_TryAsync_TurnsTheCallBackIntoAResult()
    {
        static Task<int> CallRemoteService() => Task.FromException<int>(TooYoung.ToRpcException());

        var result = await Result.TryAsync(CallRemoteService, (RpcException ex) => ex.ToError());

        Assert.Equal("user.too_young", result.Error.Code);
        Assert.Equal(ErrorType.BusinessRule, result.Error.Type);
    }
}
