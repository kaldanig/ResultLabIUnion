# Starting a gRPC service from the template

`dotnet new resultunion-grpc` creates the products feature of [the Minimal API template](templates.md) behind gRPC
instead of HTTP: the same value objects, catalogs and use cases, a `.proto` contract, a service that turns failures
into rich gRPC statuses, and tests that call it through the generated client. This page walks through what it
creates, what a client receives, and how to add a feature.

## Create the project

```powershell
dotnet new install ResultUnion.Templates
```

```powershell
dotnet new resultunion-grpc -n Catalog
```

`--database sqlite` stores the products in SQLite through EF Core, as `resultunion-api --database sqlite` does, and
`--no-tests` leaves the test project out.

```text
Catalog/
├── Catalog.slnx
├── src/Catalog/
│   ├── Program.cs
│   ├── GrpcReflection.cs           reflection for every service, with the status detail types
│   ├── Protos/products.proto       the contract; every file in Protos/ is compiled
│   ├── Data/AppDbContext.cs        with --database sqlite
│   └── Products/
│       ├── ProductName.cs, Price.cs, Product.cs    value objects and the entity, with their catalogs
│       ├── ProductRepository.cs    interface and an in-memory implementation
│       ├── ProductService.cs       the use cases
│       └── ProductCatalogService.cs the gRPC service
└── tests/Catalog.Tests/
    ├── ProductServiceTests.cs      the use cases, without gRPC
    ├── ProductCatalogTests.cs      the service through the generated client
    └── ReflectionTests.cs          what grpcurl and Postman are told
```

## How the service is built

The use cases do not know they are behind gRPC: `ProductService` is the one `resultunion-api` has, except that a
product's id arrives as text, since protobuf has no GUID. Each method of the service maps the request to the same
command and ends the pipeline with `GetValueOrThrowRpcAsync`, which returns the reply or throws an `RpcException`:

```csharp
    public override Task<ProductReply> CreateProduct(CreateProductRequest request, ServerCallContext context) =>
        service.CreateAsync(new CreateProduct(request.Name, request.HasPrice ? (decimal)request.Price : null), context.CancellationToken)
            .MapAsync(ToReply)
            .GetValueOrThrowRpcAsync(Domain);
```

Failures are not part of the messages. The status code comes from the error's type (Validation is
`INVALID_ARGUMENT`, NotFound `NOT_FOUND`, Conflict `FAILED_PRECONDITION`, Concurrency `ABORTED`), and the status
details carry the rest: an `ErrorInfo` with the code, the type and the arguments, and, for validation, a `BadRequest`
with every invalid field. The price is `optional double` in the contract, so a missing price is `price.required` and
not a price of 0; an id that does not parse is `product.invalid_id` on the field `id`.

## What a client receives

The service listens on `http://localhost:5090` (HTTP/2) and has reflection, so grpcurl calls it without the `.proto`
file. A product with an empty name and a negative price:

```powershell
grpcurl -plaintext -d '{ "name": "", "price": -1 }' localhost:5090 catalog.products.v1.ProductCatalog/CreateProduct
```

```text
ERROR:
  Code: InvalidArgument
  Message: One or more validation errors occurred.
  Details:
  1)	{
    	  "@type": "type.googleapis.com/google.rpc.ErrorInfo",
    	  "domain": "catalog",
    	  "metadata": {
    	    "code": "validation.failed",
    	    "code:PRICE_NOT_POSITIVE": "price.not_positive",
    	    "code:PRODUCT_NAME_REQUIRED": "product_name.required",
    	    "type": "Validation"
    	  },
    	  "reason": "VALIDATION_FAILED"
    	}
  2)	{
    	  "@type": "type.googleapis.com/google.rpc.BadRequest",
    	  "field_violations": [
    	    {
    	      "field": "name",
    	      "description": "The product name is required.",
    	      "reason": "PRODUCT_NAME_REQUIRED"
    	    },
    	    {
    	      "field": "price",
    	      "description": "The price must be greater than zero; -1 is not.",
    	      "reason": "PRICE_NOT_POSITIVE"
    	    }
    	  ]
    	}
```

grpcurl can show the details as JSON because `AddGrpcReflectionWithErrorDetails` (`GrpcReflection.cs`) adds
`error_details.proto` to what reflection describes; by default it describes only the services' own messages. It also
finds every service mapped with `MapGrpcService`, so a new one needs no change there. The project's README has a
grpcurl call for each outcome.

A .NET client turns a failed call back into an `Error`, with the same codes and fields. The tests call the service
through the generated client over `WebApplicationFactory`'s in-memory server, and only an `RpcException` becomes an
error:

```csharp
        return Result.TryAsync(() => _client.CreateProductAsync(request, cancellationToken: Ct).ResponseAsync, (RpcException ex) => ex.ToError());
```

## Adding a feature

From the solution folder, after a `dotnet restore`, the feature templates write a gRPC feature with `--grpc`:

```powershell
dotnet new resultunion-feature -n Supplier -o src/Catalog --grpc
```

```powershell
dotnet new resultunion-feature-tests -n Supplier -o tests/Catalog.Tests --grpc
```

Instead of endpoints, the feature gets its contract in `Protos/suppliers.proto` and `SupplierCatalogService`, whose
methods end with `GetValueOrThrowRpcAsync`; `Supplier.ParseId` turns an id that is not a GUID into
`supplier.invalid_id` on the field `id`. Register it in `Program.cs` with `builder.Services.AddSuppliers()` and
`app.MapSuppliers()`; the project compiles the new contract and reflection finds the new service by themselves.

`--crud` adds `RenameSupplier` and `RemoveSupplier` with the version the client read, a stale one being `ABORTED`
with `supplier.changed_concurrently`, and `--database sqlite` stores the feature in the application's DbContext (apply
`SupplierConfiguration` in `OnModelCreating`). The tests call the service through the generated client and read each
failure back with `ToError()`. A value object comes from `resultunion-valueobject`, as in any other project.

The rest of growing a feature, from value objects to the store's answers, is the same as for the Minimal API:
[Adding a feature of your own](templates.md#adding-a-feature-of-your-own).
