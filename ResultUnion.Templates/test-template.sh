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
(cd "$work/Shop" && dotnet build -warnaserror && dotnet test)
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
