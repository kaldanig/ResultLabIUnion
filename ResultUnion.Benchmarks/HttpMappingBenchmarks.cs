using System.Net;
using System.Net.Http.Headers;
using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ResultUnion.AspNetCore;
using ResultUnion.Http;

namespace ResultUnion.Benchmarks;

/// <summary>
/// What a failure costs over HTTP (docs/BENCHMARKS.md, "HTTP errors"), the counterpart of the gRPC benchmarks: the
/// endpoint turns the error into an RFC 9457 problem and writes it as JSON, and a client reads the problem back into
/// an error with ResultUnion.Http. A success goes through none of this.
/// </summary>
public class HttpMappingBenchmarks
{
    [Params("not found", "two invalid fields")]
    public string Failure { get; set; } = "not found";

    private Error _error = null!;
    private byte[] _body = null!;
    private HttpStatusCode _status;
    private IServiceProvider _services = null!;

    [GlobalSetup]
    public async Task Setup()
    {
        _error = Failure == "not found"
            ? Error.Create("user.not_found", "No user with the email ann@example.com.", ErrorType.NotFound).WithArgument("email", "ann@example.com")
            : Error.Validation(
            [
                Error.Create("email.invalid_format", "'ann@' is not a valid email.", ErrorType.Validation).WithArgument("value", "ann@").WithField("email"),
                Error.Create("password.too_short", "Password must be at least 8 characters.", ErrorType.Validation).WithArgument("minimumLength", 8).WithField("password"),
            ]);
        _services = new ServiceCollection().AddLogging().BuildServiceProvider();

        // The response a client receives, written once by the server side below.
        var context = NewContext();
        await _error.ToHttpResult().ExecuteAsync(context);
        _status = (HttpStatusCode)context.Response.StatusCode;
        _body = ((MemoryStream)context.Response.Body).ToArray();
    }

    [Benchmark(Baseline = true, Description = "endpoint: Error to ProblemHttpResult")]
    public int ToProblem() => _error.ToHttpResult().StatusCode;

    [Benchmark(Description = "endpoint: Error to problem response (written as JSON)")]
    public async Task<long> WriteProblem()
    {
        var context = NewContext();
        await _error.ToHttpResult().ExecuteAsync(context);
        return context.Response.Body.Length;
    }

    [Benchmark(Description = "client: problem response to Error")]
    public async Task<int> ReadProblem()
    {
        using var response = new HttpResponseMessage(_status) { Content = new ByteArrayContent(_body) };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/problem+json");
        var result = await response.ReadResultAsync<string>();
        return result.TryGetError(out var error) ? error.Code.Length : 0;
    }

    private DefaultHttpContext NewContext() => new()
    {
        RequestServices = _services,
        Response = { Body = new MemoryStream() },
    };
}
