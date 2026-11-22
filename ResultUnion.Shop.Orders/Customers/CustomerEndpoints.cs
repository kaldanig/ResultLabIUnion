using ResultUnion.AspNetCore;

namespace ResultUnion.Shop.Orders.Customers;

public sealed record CreateCustomerRequest(string? Name, string? Email);

/// <summary>A new name, with the version the client read: a stale version is 409 with <c>changed_concurrently</c>.</summary>
public sealed record RenameCustomerRequest(string? Name, int Version);

public sealed record CustomerResponse(Guid Id, string Name, string Email, int Version)
{
    public static CustomerResponse From(Customer customer) => new(customer.Id, customer.Name.Value, customer.Email.Value, customer.Version);
}

public static class CustomerEndpoints
{
    public static IEndpointRouteBuilder MapCustomers(this IEndpointRouteBuilder app)
    {
        var customers = app.MapGroup("/customers").WithTags("Customers");

        customers.MapPost("/", (CreateCustomerRequest request, CustomerService service, CancellationToken ct) =>
                service.CreateAsync(new CreateCustomer(request.Name, request.Email), ct)
                    .MapAsync(CustomerResponse.From)
                    .ToHttpResultAsync(customer => TypedResults.Created($"/customers/{customer.Id}", customer)))
            .ProducesErrors(ErrorType.Validation, ErrorType.Conflict);

        customers.MapGet("/{id:guid}", (Guid id, CustomerService service, CancellationToken ct) =>
                service.FindAsync(id, ct).MapAsync(CustomerResponse.From).ToHttpResultAsync())
            .ProducesErrors(ErrorType.NotFound);

        customers.MapPut("/{id:guid}", (Guid id, RenameCustomerRequest request, CustomerService service, CancellationToken ct) =>
                service.RenameAsync(new RenameCustomer(id, request.Name, request.Version), ct)
                    .MapAsync(CustomerResponse.From)
                    .ToHttpResultAsync())
            .ProducesErrors(ErrorType.Validation, ErrorType.NotFound, ErrorType.Concurrency);

        customers.MapDelete("/{id:guid}", (Guid id, int version, CustomerService service, CancellationToken ct) =>
                service.DeleteAsync(id, version, ct).ToHttpResultAsync())
            .ProducesErrors(ErrorType.NotFound, ErrorType.Concurrency);

        return app;
    }
}
