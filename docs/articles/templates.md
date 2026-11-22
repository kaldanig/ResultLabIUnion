# Starting a project from the template

`dotnet new resultunion-api` creates a Minimal API with one complete feature, products, and the tests for it. It is
the quickest way to see the library's conventions working together, and a shape to copy for your own features. This
article walks through what the template creates and how to grow it.

## Create the project

```powershell
dotnet new install ResultUnion.Templates
```

```powershell
dotnet new resultunion-api -n Shop
```

`-n` names the solution, the projects and their namespaces (`Shop`, `Shop.Products`, `Shop.Tests`). `--no-tests`
leaves the test project out, and `--database sqlite` stores the products in SQLite through EF Core instead of in
memory. The projects reference the ResultUnion packages of the template's own version, so
`dotnet new install ResultUnion.Templates::1.0.0-rc.1` creates a project on the release candidate.

```text
Shop/
├── Shop.slnx
├── src/Shop/
│   ├── Program.cs
│   ├── Shop.http                  requests for every outcome
│   ├── Data/AppDbContext.cs       with --database sqlite: the database and its value conversions
│   └── Products/
│       ├── ProductName.cs         value object with its Errors catalog
│       ├── Price.cs               value object with its Errors catalog
│       ├── Product.cs             entity with its Errors catalog
│       ├── ProductRepository.cs   interface and an in-memory implementation
│       ├── EfProductRepository.cs with --database sqlite: EF Core, a unique index on the name
│       ├── ProductService.cs      the use cases
│       └── ProductEndpoints.cs    POST /products, GET /products/{id}
└── tests/Shop.Tests/
    ├── ProductServiceTests.cs     the use cases, without HTTP
    └── ProductEndpointTests.cs    the API over HTTP, with WebApplicationFactory
```

## Run it

```powershell
dotnet run --project src/Shop
```

```powershell
dotnet test
```

`src/Shop/Shop.http` sends each request below; in Development the OpenAPI document is at `/openapi/v1.json`, and it
lists the problem responses each endpoint can return.

`POST /products` with `{ "name": "Coffee", "price": 4.5 }` creates the product: `201 Created`, with a `Location` header.

```json
{
  "id": "d679b823-dcd7-4fec-bfd3-f66fcc525031",
  "name": "Coffee",
  "price": 4.5
}
```

`POST /products` with `{ "name": "", "price": -1 }` reports every invalid field at once: `400`, an RFC 9457 problem
whose `code` says what kind of failure it is and whose `details` give each field's code and arguments (the `traceId` is
left out here):

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "detail": "One or more validation errors occurred.",
  "errors": {
    "name": [ "The product name is required." ],
    "price": [ "The price must be greater than zero; -1 is not." ]
  },
  "code": "validation.failed",
  "category": "Validation",
  "details": [
    { "code": "product_name.required", "field": "name", "message": "The product name is required." },
    {
      "code": "price.not_positive",
      "field": "price",
      "message": "The price must be greater than zero; -1 is not.",
      "arguments": { "amount": -1 }
    }
  ]
}
```

The same name again (`"coffee"`) is `409` with the code `product.name_taken` and the argument `name`; an unknown id is
`404` with `product.not_found` and the argument `id`. A client switches on `code` and builds its own message from the
arguments; it never parses `detail`.

## How the feature is built

**Value objects own their errors.** Each declares its codes once, in a nested `Errors` catalog. A code is a contract
with clients, so it is never invented at the call site:

```csharp
    public static class Errors
    {
        public static readonly Error Required =
            Error.Create("product_name.required", "The product name is required.", ErrorType.Validation);

        public static Error TooLong(int length) =>
            Error.Create("product_name.too_long", $"The product name has {length} characters; at most {MaxLength} are allowed.", ErrorType.Validation)
                .WithArgument("length", length)
                .WithArgument("maxLength", MaxLength);
    }
```

`ProductName.Create` returns `Result<ProductName>`: the catalog error or the value. It does not set `Field`: whether the
request calls the input `name` or `title` is the caller's business.

**A use case is one pipeline.** `CombineAll` validates every input at once and sets the field names; the product is
stored only when every input is valid, and a name that is taken is the store's answer, a failure that keeps the rest of
the chain from running:

```csharp
    public Task<Result<Product>> CreateAsync(CreateProduct command, CancellationToken ct) =>
        Result.CombineAll(                                                      // every invalid field at once
                ProductName.Create(command.Name).MapError(e => e.WithField("name")),
                Price.Create(command.Price).MapError(e => e.WithField("price")))
            .Map(Product.Create)                                                // (name, price) => Product
            .EnsureAsync(product => products.AddAsync(product, ct))             // a taken name fails here
            .RecordAsync("products.create");                                    // failures counted by code
```

`IProductRepository.AddAsync` returns a `Task<Result>`, so `EnsureAsync` keeps the product on success. With
`--database sqlite` the EF Core repository checks the name first and also catches the unique index's violation, for
two requests that check at the same time:

```csharp
        catch (DbUpdateException ex) when (ex.InnerException is SqliteException { SqliteExtendedErrorCode: 2067 })   // UNIQUE constraint failed
        {
            db.Entry(product).State = EntityState.Detached;
            return Product.Errors.NameTaken(product.Name);
        }
```

The database file (`products.db`) is created on the first start with `EnsureCreated`; a real application moves to
migrations (`dotnet ef migrations add Initial`). The HTTP tests point the application at a temporary file of their
own.

`RecordAsync` counts failures by code in OpenTelemetry (`resultunion.errors`), so a dashboard shows which errors
happen and how often.

**An endpoint maps the result once.** The value becomes a response type; a failure becomes a problem response with the
status of its `ErrorType`. `ProducesErrors` documents those responses in OpenAPI:

```csharp
        products.MapPost("/", (CreateProductRequest request, ProductService service, CancellationToken ct) =>
                service.CreateAsync(new CreateProduct(request.Name, request.Price), ct)
                    .MapAsync(ProductResponse.From)
                    .ToHttpResultAsync(product => TypedResults.Created($"/products/{product.Id}", product)))
            .ProducesErrors(ErrorType.Validation, ErrorType.Conflict);
```

**Tests read the outcome in one line** with ResultUnion.Testing, and assert on codes and fields, never on messages:

```csharp
        var error = await _service.CreateAsync(new CreateProduct("", -1m), Ct).ShouldFailAsync("validation.failed");

        error.ShouldHaveFieldErrors(("name", "product_name.required"), ("price", "price.not_positive"));
```

## Adding a feature of your own

The whole way, from the solution folder (the item templates read the namespace from the restored projects that `-o`
points at):

```powershell
dotnet new resultunion-feature -n Order -o src/Shop
```

```powershell
dotnet new resultunion-feature-tests -n Order -o tests/Shop.Tests
```

Then add `builder.Services.AddOrders();` and `app.MapOrders();` (with `using Shop.Orders;`) to `Program.cs`, and
`dotnet test` runs the feature's eight tests with the rest. The sections below explain what the two templates write.

Copy the shape of `Products` for each new concept, say `Orders`. The package's feature template writes that shape for
you, in the folder `Orders/` of the current project:

```powershell
dotnet new resultunion-feature -n Order
```

It creates `Order` with `not_found` and `name_taken`, `OrderName`, `IOrderRepository` with an in-memory store,
`OrderService` (create and find) and endpoints at `/orders`; register them in `Program.cs` with
`builder.Services.AddOrders()` and `app.MapOrders()`. `--plural Categories` sets the plural where adding an s is wrong.

With `--database sqlite` the orders go into the application's DbContext instead: `EfOrderRepository` (the same
pre-check and unique-constraint catch as the products) and `OrderConfiguration`, registered with
`builder.Services.AddOrders<AppDbContext>()` and applied in `OnModelCreating` with
`modelBuilder.ApplyConfiguration(new OrderConfiguration())`.

With `--crud` an order can also be renamed and deleted, with optimistic concurrency. `Order` gets a `Version`, and
`Rename` returns a new instance with the next one. A client sends the version it read: `PUT /orders/{id}` with `name`
and `version`, `DELETE /orders/{id}?version=2`. The service checks it first, for a clear answer, and the store checks it
again as it writes, so two clients that read the same version cannot both succeed. In memory that is one step under
the store's lock; in SQLite it is `ExecuteUpdate` / `ExecuteDelete` with `Version == expected` in the `WHERE`, and no
row written means the order is gone (`order.not_found`, 404) or changed (`order.changed_concurrently`,
`ErrorType.Concurrency`, 409, with the `id` and the `version` the client had). The client's answer to a 409 is to read
the order again and retry. The [EF Core article](ef-core.md) shows the same error from a row version with
`DbUpdateConcurrencyException`.

Its tests come from a second template, run in the test project:

```powershell
dotnet new resultunion-feature-tests -n Order
```

`OrderServiceTests` checks each code the use cases return (`order_name.required` with its field, `order.name_taken`,
`order.not_found`) and `OrderEndpointTests` what a client receives (201 with the location, 400, 409, 404); add
`--database sqlite` for an application that stores in SQLite, and `--crud` for the rename and delete tests (a stale
version is `changed_concurrently`, a deleted order 404). Then grow the feature step by step:

1. **Value objects** for the inputs that have rules (`Quantity`, `OrderNote`), each with its `Errors` catalog and a
   `Create` that returns a result. An input with a rule is a value object even when it is a single string; the entity
   then takes only validated parts. The value object template writes one in the shape of `ProductName`, in the
   current folder:

   ```powershell
   dotnet new resultunion-valueobject -n OrderNote --max-length 500
   ```

   The codes and messages come from the name (`order_note.required`, `order_note.too_long`, "The order note is
   required."), and the namespace is the project's `RootNamespace`. That namespace is read from a restored project;
   in a new project, or for a subfolder, pass `--namespace Shop.Orders`. A number takes `--value-type int` or
   `decimal` with `--min` and `--max` (`dotnet new resultunion-valueobject -n Quantity --value-type int --max 99`,
   which fails with `quantity.out_of_range`). Then change the rules to what the value needs.
2. **The entity** (`Order`) with its own catalog for the errors about it: `not_found`, and the domain rules
   (`order.already_shipped` as a `BusinessRule`, a conflict as `Conflict`).
3. **A repository interface** that returns `null` for a missing entity; the service turns that into the catalog error
   with `ToResultAsync(Order.Errors.NotFound(id))`.
4. **A service** whose methods are pipelines: `CombineAll` for the inputs (with `WithField` for each), then `Bind`,
   `EnsureAsync` and `TapAsync` for the steps, ending with `RecordAsync("orders.place")`.
5. **Endpoints** that map the value to a response type and end with `ToHttpResultAsync`, plus `ProducesErrors` with
   the categories the use case can fail with.
6. **Tests** for each code a use case can return, with `ShouldFailAsync("code")`, and one HTTP test per endpoint for
   what a client receives.

Build after each step: the ResultUnion analyzers point out the usual slips, such as a result that is discarded, `Map`
where `Bind` belongs, or an endpoint that returns a result instead of a response (RU0001, RU0003, RU0010).

For storage, start with `--database sqlite`, or put another database behind the same interface; the
[EF Core article](ef-core.md) shows lookups, unique constraints and concurrency as results. For checks that apply to
every use case (validation, authorisation, transactions), see [pipeline behaviors](pipeline-behaviors.md), and for more
on the assertions, [testing](testing.md).
