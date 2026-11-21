using ResultUnion.Samples.Domain;

namespace ResultUnion.Samples.Application;

// Application commands: raw input from any host. The Web API maps its JSON requests to these, the gRPC service maps its
// protobuf messages; the use cases never see HTTP or gRPC types.

/// <summary>Registers a user. <c>Age</c> is raw text, so invalid input reaches the domain and gets its error code.</summary>
public sealed record RegisterUser(string? Email, string? Password, string? Age);

/// <summary>Places an order; each line is validated by <see cref="OrderLine.Create"/>.</summary>
public sealed record PlaceOrder(string? CustomerEmail, IReadOnlyList<OrderLineInput>? Lines);
