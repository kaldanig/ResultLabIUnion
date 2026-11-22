# ResultUnionApi

A Minimal API built with [ResultUnion](https://kaldanig.github.io/ResultLabIUnion/), the Result pattern on C# 15 unions.

```powershell
dotnet run --project src/ResultUnionApi
```

```powershell
dotnet test
```

`src/ResultUnionApi/ResultUnionApi.http` has requests for every outcome; the OpenAPI document is at `/openapi/v1.json`
in Development.

## What is where

| File | Shows |
|---|---|
| `Products/ProductName.cs`, `Price.cs` | Value objects. Each declares its errors in a nested `Errors` catalog with stable codes (`product_name.required`). |
| `Products/Product.cs` | The entity, created from validated parts, with its own catalog (`product.not_found`, `product.name_taken`). |
| `Products/ProductService.cs` | The use cases, each one pipeline: `CombineAll` reports every invalid field, `EnsureAsync` the store's answer for a taken name, `RecordAsync` counts failures. |
<!--#if (Sqlite)-->
| `Data/AppDbContext.cs`, `Products/EfProductRepository.cs` | SQLite through EF Core: value objects stored as their value, a unique index on the name, its violation turned into `product.name_taken`. The database (`products.db`) is created on the first start; move to migrations (`dotnet ef migrations add Initial`) for a real application. |
<!--#endif-->
| `Products/ProductEndpoints.cs` | `ToHttpResultAsync`: a success is the typed response, a failure an RFC 9457 problem with `code`, `arguments` and per-field `details`. |
| `tests/` | `ShouldSucceedAsync` / `ShouldFailAsync("code")` from ResultUnion.Testing, and HTTP tests with `WebApplicationFactory`. |

To add a feature, copy the shape of `Products`: value objects with catalogs, a service that returns `Result<T>`, and
endpoints that map the value to a response. The ResultUnion analyzers (RU0001–RU0010) check the conventions as you build.
