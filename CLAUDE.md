# ResultUnion

A Result pattern library for .NET 11 built on C# 15 unions, with ASP.NET Core and gRPC integrations, analyzers,
benchmarks and demos. `README.md` describes the API, `docs/DESIGN.md` the decisions behind it.

## Writing code that uses ResultUnion

Before writing, changing or reviewing C# that uses ResultUnion (the demos, the samples, a test that exercises a
pipeline), read `.claude/skills/resultunion/SKILL.md` and the reference file it points to. The library's names and
conventions differ from other Result libraries (`Bind`, not `Then`; `Task`, not `ValueTask`; error catalogs; who sets
`Field`), and code written from memory breaks them. Do this even when the task looks small.

Changes to the library itself (`ResultUnion/`, `ResultUnion.AspNetCore/`, `ResultUnion.Grpc/`) follow
`docs/DESIGN.md`; record a new decision there when one is made.

## Build and test

```powershell
dotnet build ResultLabIUnion.slnx
```

```powershell
dotnet test --solution ResultLabIUnion.slnx
```

- Every `net11.0` project builds with .NET 11 Runtime Async (`Directory.Build.targets`). `-p:EnableRuntimeAsync=false`
  builds the classic state machines into `artifacts/state-machines/`; the tests must pass both ways.
- The ResultUnion analyzers (RU0001–RU0003) run on every project; fix their warnings rather than suppressing them.
- The public API of each package is recorded in `PublicAPI.Shipped.txt` / `PublicAPI.Unshipped.txt`; a new or changed
  public member must be added to `PublicAPI.Unshipped.txt` (RS0016/RS0017), and user-visible changes go under
  `## [Unreleased]` in `CHANGELOG.md`. Releasing is described in `docs/RELEASING.md`.
