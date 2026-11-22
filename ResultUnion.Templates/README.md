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

The projects reference the ResultUnion packages of the same version as the template.

```powershell
dotnet new resultunion-valueobject -n CustomerEmail --max-length 254
```

`resultunion-valueobject` adds a string value object to an existing project: `Create` returns `Result<T>`, and its
`Errors` catalog declares `customer_email.required` and `customer_email.too_long` (with the `length` and `maxLength`
arguments). The namespace is the project's `RootNamespace`, read from a restored project; `--namespace` sets another.
