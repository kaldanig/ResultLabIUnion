# ResultUnionGrpc

A gRPC service built with [ResultUnion](https://kaldanig.github.io/ResultLabIUnion/), the Result pattern on C# 15 unions.

```powershell
dotnet run --project src/ResultUnionGrpc
```

```powershell
dotnet test
```

## Calling it

The service listens on `http://localhost:5090` (HTTP/2 only) and has reflection, so
[grpcurl](https://github.com/fullstorydev/grpcurl) can call it without the `.proto` file. One call per outcome, in
PowerShell 7.3 or later (earlier versions strip the quotes inside the JSON):

```powershell
grpcurl -plaintext localhost:5090 describe resultuniongrpc.products.v1.ProductCatalog
```

A product is created, and the reply has its id:

```powershell
grpcurl -plaintext -d '{ "name": "Coffee", "price": 4.5 }' localhost:5090 resultuniongrpc.products.v1.ProductCatalog/CreateProduct
```

The same call again: `FailedPrecondition`, a product named 'Coffee' already exists (`product.name_taken`).

Every invalid field at once: `InvalidArgument`, with a field violation for `name` (`product_name.required`) and one
for `price` (`price.not_positive`):

```powershell
grpcurl -plaintext -d '{ "name": "", "price": -1 }' localhost:5090 resultuniongrpc.products.v1.ProductCatalog/CreateProduct
```

No price is not a price of 0: `InvalidArgument` with `price.required`:

```powershell
grpcurl -plaintext -d '{ "name": "Tea" }' localhost:5090 resultuniongrpc.products.v1.ProductCatalog/CreateProduct
```

A product by its id (put in the id from the first reply); an unknown id is `NotFound` (`product.not_found`):

```powershell
grpcurl -plaintext -d '{ "id": "00000000-0000-0000-0000-000000000001" }' localhost:5090 resultuniongrpc.products.v1.ProductCatalog/GetProduct
```

An id that is not a GUID: `InvalidArgument` with `product.invalid_id` on the field `id`:

```powershell
grpcurl -plaintext -d '{ "id": "42" }' localhost:5090 resultuniongrpc.products.v1.ProductCatalog/GetProduct
```

For a failure, grpcurl prints the status code, the message and the status details: an `ErrorInfo` with the code, the
type and the arguments in `metadata`, and, for validation, a `BadRequest` with every invalid field. It can read them
because `GrpcReflection.cs` adds `error_details.proto` to what reflection describes. A .NET client reads them back as an
`Error` with `ToError()`, as the tests do.

## What is where

| File | Shows |
|---|---|
| `Protos/products.proto` | The contract. Failures are not in the messages: they travel in the call's status. |
| `Products/ProductName.cs`, `Price.cs` | Value objects. Each declares its errors in a nested `Errors` catalog with stable codes (`product_name.required`). |
| `Products/Product.cs` | The entity, created from validated parts, with its own catalog (`product.invalid_id`, `product.not_found`, `product.name_taken`). |
| `Products/ProductService.cs` | The use cases, each one pipeline: `CombineAll` reports every invalid field, `EnsureAsync` the store's answer for a taken name, `RecordAsync` counts failures. |
<!--#if (Sqlite)-->
| `Data/AppDbContext.cs`, `Products/EfProductRepository.cs` | SQLite through EF Core: value objects stored as their value, a unique index on the name, its violation turned into `product.name_taken`. The database (`products.db`) is created on the first start; move to migrations (`dotnet ef migrations add Initial`) for a real application. |
<!--#endif-->
| `Program.cs` | The host: the services, the store and reflection. |
| `GrpcReflection.cs` | `AddGrpcReflectionWithErrorDetails`: reflection for every service mapped with `MapGrpcService`, plus `error_details.proto`, so grpcurl shows a failure's details as JSON. A new service needs no change here. |
| `Products/ProductCatalogService.cs` | `GetValueOrThrowRpcAsync`: a success is the reply, a failure an `RpcException` whose status code comes from the error's type, with an `ErrorInfo` (the code and arguments) and, for validation, a `BadRequest` with every invalid field. |
| `tests/` | `ShouldSucceedAsync` / `ShouldFailAsync("code")` from ResultUnion.Testing, and calls through the generated client with `WebApplicationFactory`, where `ToError()` turns a failed call back into an `Error`, and a check that reflection describes the status detail types. |

The use cases do not know they are behind gRPC: the same `ProductService` could serve a Minimal API
(`dotnet new resultunion-api` shows that host). To add a feature, copy the shape of `Products`: value objects with
catalogs, a service that returns `Result<T>`, and a gRPC service that maps the value to a reply. The ResultUnion
analyzers (RU0001–RU0010) check the conventions as you build.
