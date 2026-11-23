using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Consul;

namespace ResultUnion.Consul.Tests;

/// <summary>
/// The part of a Consul agent's HTTP API the sample uses, in memory: service registration with a TTL check, check
/// updates, deregistration, the health endpoint (passing instances only) and the key/value store. As in a real agent, a
/// TTL check starts critical, and an update of a check the agent does not know is a 404. <see cref="IsDown"/> makes
/// every call fail as a refused connection does.
/// </summary>
public sealed class FakeConsul : HttpMessageHandler
{
    private readonly ConcurrentDictionary<string, Instance> _services = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _values = new(StringComparer.Ordinal);

    public bool IsDown { get; set; }

    /// <summary>A Consul client whose calls come here.</summary>
    public IConsulClient CreateClient()
    {
        var http = new HttpClient(this, disposeHandler: false);
        http.DefaultRequestHeaders.Accept.Add(new("application/json"));     // the client insists on it
        return new ConsulClient(new ConsulClientConfiguration { Address = new Uri("http://consul:8500") }, http);
    }

    /// <summary>The ids of the instances of <paramref name="service"/> whose check passes.</summary>
    public IReadOnlyList<string> Passing(string service) =>
        [.. _services.Values.Where(i => i.Name == service && i.Status == "passing").Select(i => i.Id).Order(StringComparer.Ordinal)];

    public IReadOnlyList<string> Registered(string service) =>
        [.. _services.Values.Where(i => i.Name == service).Select(i => i.Id).Order(StringComparer.Ordinal)];

    /// <summary>Forgets every registration, as a dev agent does when it restarts.</summary>
    public void Forget() => _services.Clear();

    public void Put(string key, string value) => _values[key] = value;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (IsDown)
            throw new HttpRequestException("Connection refused (consul:8500)");

        var path = request.RequestUri!.AbsolutePath;
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

        return (request.Method.Method, path) switch
        {
            ("PUT", "/v1/agent/service/register") => Register(JsonNode.Parse(body!)!),
            ("PUT", _) when path.StartsWith("/v1/agent/check/update/", StringComparison.Ordinal) =>
                Update(Uri.UnescapeDataString(path["/v1/agent/check/update/".Length..]), JsonNode.Parse(body!)!["Status"]!.GetValue<string>()),
            ("PUT", _) when path.StartsWith("/v1/agent/service/deregister/", StringComparison.Ordinal) =>
                Deregister(Uri.UnescapeDataString(path["/v1/agent/service/deregister/".Length..])),
            ("GET", _) when path.StartsWith("/v1/health/service/", StringComparison.Ordinal) =>
                Health(Uri.UnescapeDataString(path["/v1/health/service/".Length..])),
            ("GET", _) when path.StartsWith("/v1/kv/", StringComparison.Ordinal) =>
                List(Uri.UnescapeDataString(path["/v1/kv/".Length..])),
            _ => Answer(HttpStatusCode.MethodNotAllowed, $"No fake for {request.Method} {path}"),
        };
    }

    private HttpResponseMessage Register(JsonNode registration)
    {
        var id = registration["ID"]!.GetValue<string>();
        _services[id] = new Instance(
            id,
            registration["Name"]!.GetValue<string>(),
            registration["Address"]?.GetValue<string>() ?? "",
            registration["Port"]!.GetValue<int>(),
            "critical");
        return Answer(HttpStatusCode.OK, "");
    }

    private HttpResponseMessage Update(string checkId, string status)
    {
        var id = checkId.StartsWith("service:", StringComparison.Ordinal) ? checkId["service:".Length..] : checkId;
        if (!_services.TryGetValue(id, out var instance))
            return Answer(HttpStatusCode.NotFound, $"Unknown check ID \"{checkId}\".");

        _services[id] = instance with { Status = status };
        return Answer(HttpStatusCode.OK, "");
    }

    private HttpResponseMessage Deregister(string id)
    {
        _services.TryRemove(id, out _);
        return Answer(HttpStatusCode.OK, "");
    }

    private HttpResponseMessage Health(string service) => Answer(HttpStatusCode.OK, JsonSerializer.Serialize(
        _services.Values.Where(i => i.Name == service && i.Status == "passing").Select(i => new
        {
            Node = new { Node = "fake", Address = "127.0.0.1" },
            Service = new { i.Id, Service = i.Name, i.Address, i.Port },
            Checks = new[] { new { CheckID = $"service:{i.Id}", Status = i.Status } },
        })));

    private HttpResponseMessage List(string prefix)
    {
        var pairs = _values.Where(pair => pair.Key.StartsWith(prefix, StringComparison.Ordinal))
            .Select(pair => new { pair.Key, Value = Convert.ToBase64String(Encoding.UTF8.GetBytes(pair.Value)), Flags = 0 })
            .ToList();

        // As the real agent: no key under the prefix is a 404 with an empty body.
        return pairs.Count == 0 ? Answer(HttpStatusCode.NotFound, "") : Answer(HttpStatusCode.OK, JsonSerializer.Serialize(pairs));
    }

    private static HttpResponseMessage Answer(HttpStatusCode status, string body)
    {
        var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        response.Headers.Add("X-Consul-Index", "1");
        response.Headers.Add("X-Consul-KnownLeader", "true");
        response.Headers.Add("X-Consul-LastContact", "0");
        return response;
    }

    private sealed record Instance(string Id, string Name, string Address, int Port, string Status);
}
