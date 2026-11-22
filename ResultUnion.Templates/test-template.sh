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
if [ -e "$work/Shop/src/Shop/Data" ] || [ -e "$work/Shop/src/Shop/Products/EfProductRepository.cs" ]; then
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
}
EOF
(cd "$work/Shop" && dotnet build -warnaserror && dotnet test)
echo "::endgroup::"

echo "::group::resultunion-api --database sqlite"
dotnet new resultunion-api -n Store -o "$work/Store" --database sqlite --debug:custom-hive "$hive"
cp "$work/nuget.config" "$work/Store/"
if [ ! -e "$work/Store/src/Store/Data/AppDbContext.cs" ]; then
  echo "::error::--database sqlite did not add the DbContext"
  exit 1
fi
(cd "$work/Store" && dotnet build -warnaserror && dotnet test)
echo "::endgroup::"

echo "::group::resultunion-api --no-tests"
dotnet new resultunion-api -n Lean -o "$work/Lean" --no-tests --debug:custom-hive "$hive"
cp "$work/nuget.config" "$work/Lean/"
if [ -d "$work/Lean/tests" ]; then
  echo "::error::--no-tests left the tests folder in place"
  exit 1
fi
(cd "$work/Lean" && dotnet build -warnaserror)
echo "::endgroup::"

echo "The template creates projects that build and pass their tests."
