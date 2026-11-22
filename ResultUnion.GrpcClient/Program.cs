using System.Diagnostics;
using System.Text;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;
using OpenTelemetry;
using OpenTelemetry.Trace;
using ResultUnion;
using ResultUnion.Diagnostics;
using ResultUnion.Grpc;
using ResultUnion.GrpcDemo;

// Calls ResultUnion.GrpcService over real gRPC. Every call goes through Result.TryAsync with a typed mapper, so only an
// RpcException is translated (into an Error, by ResultUnion.Grpc); the code below never catches anything itself.

Console.OutputEncoding = Encoding.UTF8;

// Traces over OTLP to OTEL_EXPORTER_OTLP_ENDPOINT, set by ResultUnion.AppHost. Each call is a span of its own, with the
// HTTP/2 request under it and the service's span under that, so one trace shows both sides of a call.
using var tracing = Sdk.CreateTracerProviderBuilder()
    .AddSource(ClientTelemetry.Source.Name)
    .AddHttpClientInstrumentation()
    .AddOtlpExporter()
    .Build();

var address = args.FirstOrDefault() ?? "http://localhost:5259";
using var channel = GrpcChannel.ForAddress(address);
var users = new Users.UsersClient(channel);
var orders = new Orders.OrdersClient(channel);

Console.WriteLine($"ResultUnion gRPC client -> {address}");

Section("Users");
await Call("RegisterUser (ok)", () => users.RegisterUserAsync(new() { Email = "bob@example.com", Password = "s3cret!pass", Age = "29" }).ResponseAsync);
await Call("RegisterUser (every invalid field)", () => users.RegisterUserAsync(new() { Email = "bob@", Password = "123", Age = "abc" }).ResponseAsync);
await Call("RegisterUser (duplicate)", () => users.RegisterUserAsync(new() { Email = "ann@example.com", Password = "s3cret!pass", Age = "40" }).ResponseAsync);
await Call("RegisterUser (too young)", () => users.RegisterUserAsync(new() { Email = "kid@example.com", Password = "s3cret!pass", Age = "15" }).ResponseAsync);
await Call("GetUser (ann)", () => users.GetUserAsync(new() { Email = "ann@example.com" }).ResponseAsync);
await Call("GetUser (zoe)", () => users.GetUserAsync(new() { Email = "zoe@example.com" }).ResponseAsync);

Section("Rate limit");
for (var attempt = 1; attempt <= 4; attempt++)
{
    await Call($"RequestPasswordReset #{attempt}", () => users.RequestPasswordResetAsync(new() { Email = "ann@example.com" }).ResponseAsync);
}

Section("Commands without a value");
await Call("DeactivateUser (ann)", () => users.DeactivateUserAsync(new() { Email = "ann@example.com" }).ResponseAsync);
await Call("DeactivateUser (ann, again)", () => users.DeactivateUserAsync(new() { Email = "ann@example.com" }).ResponseAsync);

Section("Orders");
await Call("PlaceOrder (ok)", () => orders.PlaceOrderAsync(Order("ann@example.com", ("apple", 2), ("pear", 1))).ResponseAsync);
await Call("PlaceOrder (invalid lines)", () => orders.PlaceOrderAsync(Order("ann@example.com", ("apple", 2), ("", 0), ("pear", -1))).ResponseAsync);
await Call("PlaceOrder (unknown customer)", () => orders.PlaceOrderAsync(Order("zoe@example.com", ("apple", 1))).ResponseAsync);

Section("Metadata: role");
await Call("ListUsers (no role)", () => users.ListUsersAsync(new Empty()).ResponseAsync);
await Call("ListUsers (x-role: admin)", () => users.ListUsersAsync(new Empty(), new Metadata { { "x-role", "admin" } }).ResponseAsync);

Console.WriteLine();
return;

static async Task Call<TReply>(string label, Func<Task<TReply>> call)
    where TReply : notnull
{
    using var activity = ClientTelemetry.Source.StartActivity(label);

    // Record puts the restored error on the client's span too: the same code and category as on the service's side.
    Result<TReply> result = await Result.TryAsync(call, (RpcException ex) => ex.ToError())
        .RecordAsync("grpc_client.call");

    Console.WriteLine();
    if (result.TryGetResultValue(out var reply))
    {
        Write(ConsoleColor.Green, $"  {label}  ->  OK");
        Console.WriteLine($"    {reply}");
        return;
    }

    var error = result.Error;
    Write(ConsoleColor.Yellow, $"  {label}  ->  {error.Type.ToGrpcStatusCode()} ({error.Type}{(error.IsTransient ? ", transient" : "")})");
    Console.WriteLine($"    {error.Code}: {error.Message}");

    if (error.Arguments.Count > 0)
    {
        Console.WriteLine($"    arguments: {string.Join(", ", error.Arguments.Select(pair => $"{pair.Key}={pair.Value}"))}");
    }

    foreach (var detail in error.Flatten().Where(detail => detail.Field is not null))
    {
        Console.WriteLine($"    {detail.Field,-20} {detail.Code}: {detail.Message}");
    }
}

static PlaceOrderRequest Order(string customer, params (string Product, int Quantity)[] lines) => new()
{
    CustomerEmail = customer,
    Lines = { lines.Select(line => new OrderLine { Product = line.Product, Quantity = line.Quantity }) },
};

static void Section(string title)
{
    Console.WriteLine();
    Write(ConsoleColor.Cyan, $"=== {title} ===");
}

static void Write(ConsoleColor color, string text)
{
    Console.ForegroundColor = color;
    Console.WriteLine(text);
    Console.ResetColor();
}

internal static class ClientTelemetry
{
    public static readonly ActivitySource Source = new("ResultUnion.GrpcClient");
}
