# ResultUnionGrpc

A gRPC service built with [ResultUnion](https://kaldanig.github.io/ResultLabIUnion/), the Result pattern on C# 15 unions.

```powershell
dotnet run --project src/ResultUnionGrpc
```

```powershell
dotnet test
```

The service listens on `http://localhost:5090` (HTTP/2 only) and has reflection, so grpcurl can call it without the
`.proto` file:

```powershell
grpcurl -plaintext -d '{ "name": "", "price": -1 }' localhost:5090 resultuniongrpc.products.v1.ProductCatalog/CreateProduct
```

## What is where

| File | Shows |
|---|---|
| `Protos/products.proto` | The contract. Failures are not in the messages: they travel in the call's status. |
| `Products/ProductName.cs`, `Price.cs` | Value objects. Each declares its errors in a nested `Errors` catalog with stable codes (`product_name.required`). |
| `Products/Product.cs` | The entity, created from validated parts, with its own catalog (`product.invalid_id`, `product.not_found`, `product.name_taken`). |
| `Products/ProductService.cs` | The use cases, each one pipeline: `CombineAll` reports every invalid field, `EnsureAsync` the store's answer for a taken name, `RecordAsync` counts failures. |
| `Products/ProductCatalogService.cs` | `GetValueOrThrowRpcAsync`: a success is the reply, a failure an `RpcException` whose status code comes from the error's type, with an `ErrorInfo` (the code and arguments) and, for validation, a `BadRequest` with every invalid field. |
| `tests/` | `ShouldSucceedAsync` / `ShouldFailAsync("code")` from ResultUnion.Testing, and calls through the generated client with `WebApplicationFactory`, where `ToError()` turns a failed call back into an `Error`. |

The use cases do not know they are behind gRPC: the same `ProductService` could serve a Minimal API
(`dotnet new resultunion-api` shows that host). To add a feature, copy the shape of `Products`: value objects with
catalogs, a service that returns `Result<T>`, and a gRPC service that maps the value to a reply. The ResultUnion
analyzers (RU0001–RU0010) check the conventions as you build.
