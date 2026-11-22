# ResultUnion.Templates

`dotnet new` templates for [ResultUnion](https://kaldanig.github.io/ResultLabIUnion/), the Result pattern on C# 15
unions.

```powershell
dotnet new install ResultUnion.Templates
```

```powershell
dotnet new resultunion-api -n Shop
```

`resultunion-api` creates a Minimal API with one feature (products) that follows the library's conventions: value
objects with error catalogs, a use case as one `Result` pipeline, endpoints that turn failures into RFC 9457 problem
responses, and a test project with ResultUnion.Testing. `--no-tests` leaves the test project out; `--database sqlite`
stores the products in SQLite through EF Core instead of in memory.

```powershell
dotnet new resultunion-grpc -n Catalog
```

`resultunion-grpc` creates the same feature behind gRPC: a `.proto` contract, a service whose methods end with
`GetValueOrThrowRpcAsync` (a failure becomes a status with `ErrorInfo` and, for validation, `BadRequest`), reflection
for grpcurl, and tests that call the service through the generated client and read a failed call back as an `Error`
with `ToError()`. `--no-tests` leaves the test project out.

The projects reference the ResultUnion packages of the same version as the template.

## A second feature, with its tests

From the solution folder that `resultunion-api -n Shop` created, after a `dotnet restore`:

```powershell
dotnet new resultunion-feature -n Order -o src/Shop
```

```powershell
dotnet new resultunion-feature-tests -n Order -o tests/Shop.Tests
```

Add `builder.Services.AddOrders();` and `app.MapOrders();` (with `using Shop.Orders;`) to `Program.cs`, and
`dotnet test` runs the new feature's tests with the rest. Each template is described below.

## The templates

```powershell
dotnet new resultunion-valueobject -n CustomerEmail --max-length 254
```

`resultunion-valueobject` adds a string value object to an existing project: `Create` returns `Result<T>`, and its
`Errors` catalog declares `customer_email.required` and `customer_email.too_long` (with the `length` and `maxLength`
arguments). The namespace is the project's `RootNamespace`, read from a restored project; `--namespace` sets another.
`--value-type int` or `--value-type decimal` makes a number from `--min` to `--max` instead, with
`<name>.out_of_range`.

```powershell
dotnet new resultunion-feature -n OrderLine
```

`resultunion-feature` adds a feature in the shape of the products to a Minimal API project, in the folder
`OrderLines/`: the entity and its name with error catalogs, an in-memory repository, a service whose use cases are
`Result` pipelines, and endpoints at `/order-lines`. Register it in `Program.cs` with `builder.Services.AddOrderLines()`
and `app.MapOrderLines()`. `--plural Categories` sets the plural where adding an s is wrong. `--database sqlite`
stores it through EF Core in the application's DbContext: register it with `AddOrderLines<AppDbContext>()` and apply
`OrderLineConfiguration` in `OnModelCreating`. `--crud` adds rename and delete with a version: a client sends the
version it read (`PUT /order-lines/{id}` with `name` and `version`, `DELETE /order-lines/{id}?version=2`), and a
stale one is 409 with `order_line.changed_concurrently`.

```powershell
dotnet new resultunion-feature-tests -n OrderLine
```

`resultunion-feature-tests`, run in the test project, adds the feature's tests in `OrderLines/`: one for each code its
use cases return, and its endpoints over HTTP with `WebApplicationFactory`. The application's namespace is the test
project's without `.Tests` (`--app-namespace` sets another); `--database sqlite` gives the HTTP tests a database file
of their own, and `--crud` tests rename and delete for a feature made with `--crud`.
