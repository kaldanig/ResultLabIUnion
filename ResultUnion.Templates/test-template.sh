#!/usr/bin/env bash
# Creates projects from the template package and builds and tests them, against packages built in the same run.
#
#   ResultUnion.Templates/test-template.sh <folder with ResultUnion.Templates and the ResultUnion packages>
#
# The template is installed into a private template store (--debug:custom-hive), and the projects are created outside
# the repository, so neither the user's templates nor the repository's Directory.Build files are involved. The new
# projects are built with warnings as errors: the template must be clean under the ResultUnion analyzers.
set -euo pipefail

# pwd -W gives the Windows path under Git Bash, where NuGet cannot read /f/...; Linux has plain pwd.
packages=$(cd "$1" && (pwd -W 2>/dev/null || pwd))
template=$(ls "$packages"/ResultUnion.Templates.*.nupkg | head -n 1)
work=$(mktemp -d)
hive="$work/hive"
trap 'rm -rf "$work"' EXIT

# The packages of this run first, nuget.org for everything else.
cat > "$work/nuget.config" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="this-run" value="$packages" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
EOF

dotnet new install "$template" --debug:custom-hive "$hive"

echo "::group::resultunion-api"
dotnet new resultunion-api -n Shop -o "$work/Shop" --debug:custom-hive "$hive"
cp "$work/nuget.config" "$work/Shop/"
if [[ -e "$work/Shop/src/Shop/Data" ]] || [[ -e "$work/Shop/src/Shop/Products/EfProductRepository.cs" ]]; then
  echo "::error::the in-memory project contains the SQLite files"
  exit 1
fi

# The item template, inside the new project: once with the project's root namespace, once with --namespace and a folder.
# A test in the project's test project runs what it creates. dotnet new reads RootNamespace only from a restored project.
(cd "$work/Shop/src/Shop" \
  && dotnet restore \
  && dotnet new resultunion-valueobject -n Sku --debug:custom-hive "$hive" \
  && dotnet new resultunion-valueobject -n CustomerEmail -o Customers --namespace Shop.Customers --max-length 254 --debug:custom-hive "$hive")
grep -q '^namespace Shop;' "$work/Shop/src/Shop/Sku.cs" \
  || { echo "::error::resultunion-valueobject did not use the project's root namespace"; exit 1; }
grep -q '"customer_email.required", "The customer email is required."' "$work/Shop/src/Shop/Customers/CustomerEmail.cs" \
  || { echo "::error::resultunion-valueobject did not derive the code and the message from the name"; exit 1; }
cat > "$work/Shop/tests/Shop.Tests/ValueObjectTemplateTests.cs" <<'EOF'
using Shop.Customers;

namespace Shop.Tests;

public sealed class ValueObjectTemplateTests
{
    [Fact]
    public void A_value_is_trimmed() => Assert.Equal("a@example.com", CustomerEmail.Create(" a@example.com ").ShouldSucceed().Value);

    [Fact]
    public void An_empty_value_is_required() => CustomerEmail.Create(" ").ShouldFail("customer_email.required");

    [Fact]
    public void A_long_value_has_its_lengths() =>
        CustomerEmail.Create(new string('a', 255)).ShouldFail("customer_email.too_long")
            .ShouldHaveArgument("length", 255)
            .ShouldHaveArgument("maxLength", 254);

    [Fact]
    public void The_default_length_is_100() => Sku.Create(new string('a', 101)).ShouldFail("sku.too_long");

    [Fact]
    public void An_int_is_in_its_range()
    {
        Assert.Equal(99, Quantity.Create(99).ShouldSucceed().Value);
        Quantity.Create(null).ShouldFail("quantity.required");
        Quantity.Create(100).ShouldFail("quantity.out_of_range").ShouldHaveArgument("max", 99);
    }

    [Fact]
    public void A_decimal_is_in_its_range()
    {
        Assert.Equal(0.01m, UnitPrice.Create(0.01m).ShouldSucceed().Value);
        UnitPrice.Create(0m).ShouldFail("unit_price.out_of_range").ShouldHaveArgument("min", 0.01m);
    }
}
EOF

# The feature template, registered in Program.cs as its post-action asks, and called over HTTP.
(cd "$work/Shop/src/Shop" \
  && dotnet new resultunion-valueobject -n Quantity --value-type int --max 99 --debug:custom-hive "$hive" \
  && dotnet new resultunion-valueobject -n UnitPrice --value-type decimal --min 0.01 --max 100000 --debug:custom-hive "$hive" \
  && dotnet new resultunion-feature -n OrderLine --debug:custom-hive "$hive" \
  && dotnet new resultunion-feature -n Category --plural Categories --crud --debug:custom-hive "$hive")
for file in OrderLines/OrderLine.cs OrderLines/OrderLineName.cs OrderLines/OrderLineRepository.cs OrderLines/OrderLineService.cs \
            OrderLines/OrderLineFeature.cs Categories/CategoryFeature.cs; do
  [[ -e "$work/Shop/src/Shop/$file" ]] || { echo "::error::resultunion-feature did not create $file"; exit 1; }
done
sed -i 's/^builder.Services.AddScoped<ProductService>();/&\nbuilder.Services.AddOrderLines();\nbuilder.Services.AddCategories();/; s/^app.MapProducts();/&\napp.MapOrderLines();\napp.MapCategories();/' \
  "$work/Shop/src/Shop/Program.cs"
sed -i 's/^using Shop.Products;/&\nusing Shop.Categories;\nusing Shop.OrderLines;/' "$work/Shop/src/Shop/Program.cs"
# And their tests, from the tests template, in the test project.
(cd "$work/Shop/tests/Shop.Tests" \
  && dotnet restore \
  && dotnet new resultunion-feature-tests -n OrderLine --debug:custom-hive "$hive" \
  && dotnet new resultunion-feature-tests -n Category --plural Categories --crud --debug:custom-hive "$hive")
grep -q '^using Shop.OrderLines;' "$work/Shop/tests/Shop.Tests/OrderLines/OrderLineServiceTests.cs" \
  || { echo "::error::resultunion-feature-tests did not derive the application's namespace"; exit 1; }
(cd "$work/Shop" && dotnet build -warnaserror && dotnet test)
echo "::endgroup::"

echo "::group::resultunion-api --database sqlite"
dotnet new resultunion-api -n Store -o "$work/Store" --database sqlite --debug:custom-hive "$hive"
cp "$work/nuget.config" "$work/Store/"
if [[ ! -e "$work/Store/src/Store/Data/AppDbContext.cs" ]]; then
  echo "::error::--database sqlite did not add the DbContext"
  exit 1
fi

# A feature in SQLite, in the application's DbContext, with its tests.
# From the solution folder with -o, as the README's recipe does.
(cd "$work/Store" && dotnet restore \
  && dotnet new resultunion-feature -n Supplier -o src/Store --database sqlite --crud --debug:custom-hive "$hive" \
  && dotnet new resultunion-feature-tests -n Supplier -o tests/Store.Tests --database sqlite --crud --debug:custom-hive "$hive")
[[ -e "$work/Store/src/Store/Suppliers/EfSupplierRepository.cs" ]] \
  || { echo "::error::resultunion-feature --database sqlite did not add the EF repository"; exit 1; }
sed -i 's/^builder.Services.AddScoped<ProductService>();/&\nbuilder.Services.AddSuppliers<AppDbContext>();/; s/^app.MapProducts();/&\napp.MapSuppliers();/; s/^using Store.Products;/&\nusing Store.Suppliers;/' \
  "$work/Store/src/Store/Program.cs"
sed -i 's/^        var product = modelBuilder.Entity<Product>();/        modelBuilder.ApplyConfiguration(new Store.Suppliers.SupplierConfiguration());\n&/' \
  "$work/Store/src/Store/Data/AppDbContext.cs"
(cd "$work/Store" && dotnet build -warnaserror && dotnet test)
echo "::endgroup::"

echo "::group::resultunion-api --no-tests"
dotnet new resultunion-api -n Lean -o "$work/Lean" --no-tests --debug:custom-hive "$hive"
cp "$work/nuget.config" "$work/Lean/"
if [[ -d "$work/Lean/tests" ]]; then
  echo "::error::--no-tests left the tests folder in place"
  exit 1
fi
(cd "$work/Lean" && dotnet build -warnaserror)
echo "::endgroup::"

echo "::group::resultunion-grpc"
dotnet new resultunion-grpc -n Catalog -o "$work/Catalog" --debug:custom-hive "$hive"
cp "$work/nuget.config" "$work/Catalog/"
grep -q '^package catalog.products.v1;' "$work/Catalog/src/Catalog/Protos/products.proto" \
  || { echo "::error::resultunion-grpc did not name the proto package after the project"; exit 1; }
if [[ -e "$work/Catalog/src/Catalog/Data" ]] || [[ -e "$work/Catalog/src/Catalog/Products/EfProductRepository.cs" ]]; then
  echo "::error::the in-memory gRPC project contains the SQLite files"
  exit 1
fi

# A gRPC feature with rename and delete, and its tests: the .proto lands in Protos/, which the project compiles.
(cd "$work/Catalog" && dotnet restore \
  && dotnet new resultunion-feature -n Supplier -o src/Catalog --grpc --crud --debug:custom-hive "$hive" \
  && dotnet new resultunion-feature-tests -n Supplier -o tests/Catalog.Tests --grpc --crud --debug:custom-hive "$hive")
[[ -e "$work/Catalog/src/Catalog/Protos/suppliers.proto" ]] && [[ -e "$work/Catalog/src/Catalog/Suppliers/SupplierCatalogService.cs" ]] \
  || { echo "::error::resultunion-feature --grpc did not add the contract and the service"; exit 1; }
if [[ -e "$work/Catalog/src/Catalog/Grpc" ]] || [[ -e "$work/Catalog/tests/Catalog.Tests/Suppliers/SupplierEndpointTests.cs" ]]; then
  echo "::error::resultunion-feature --grpc left the HTTP files or its Grpc folder"
  exit 1
fi
sed -i 's/^builder.Services.AddScoped<ProductService>();/&\nbuilder.Services.AddSuppliers();/; s/^app.MapGrpcService<ProductCatalogService>();/&\napp.MapSuppliers();/; s/^using Catalog.Products;/&\nusing Catalog.Suppliers;/' \
  "$work/Catalog/src/Catalog/Program.cs"
(cd "$work/Catalog" && dotnet build -warnaserror && dotnet test)
echo "::endgroup::"

echo "::group::resultunion-grpc --database sqlite"
dotnet new resultunion-grpc -n Warehouse -o "$work/Warehouse" --database sqlite --debug:custom-hive "$hive"
cp "$work/nuget.config" "$work/Warehouse/"
[[ -e "$work/Warehouse/src/Warehouse/Data/AppDbContext.cs" ]] \
  || { echo "::error::resultunion-grpc --database sqlite did not add the DbContext"; exit 1; }

# A gRPC feature in SQLite, in the application's DbContext, with its tests.
(cd "$work/Warehouse" && dotnet restore \
  && dotnet new resultunion-feature -n StorageBin -o src/Warehouse --grpc --database sqlite --debug:custom-hive "$hive" \
  && dotnet new resultunion-feature-tests -n StorageBin -o tests/Warehouse.Tests --grpc --database sqlite --debug:custom-hive "$hive")
sed -i 's/^builder.Services.AddScoped<ProductService>();/&\nbuilder.Services.AddStorageBins<AppDbContext>();/; s/^app.MapGrpcService<ProductCatalogService>();/&\napp.MapStorageBins();/; s/^using Warehouse.Products;/&\nusing Warehouse.StorageBins;/' \
  "$work/Warehouse/src/Warehouse/Program.cs"
sed -i 's/^        var product = modelBuilder.Entity<Product>();/        modelBuilder.ApplyConfiguration(new Warehouse.StorageBins.StorageBinConfiguration());\n&/' \
  "$work/Warehouse/src/Warehouse/Data/AppDbContext.cs"
(cd "$work/Warehouse" && dotnet build -warnaserror && dotnet test)
echo "::endgroup::"

echo "::group::resultunion-grpc --no-tests"
dotnet new resultunion-grpc -n LeanCatalog -o "$work/LeanCatalog" --no-tests --debug:custom-hive "$hive"
cp "$work/nuget.config" "$work/LeanCatalog/"
if [[ -d "$work/LeanCatalog/tests" ]]; then
  echo "::error::--no-tests left the tests folder in place"
  exit 1
fi
(cd "$work/LeanCatalog" && dotnet build -warnaserror)
echo "::endgroup::"

echo "The templates create projects that build and pass their tests."
