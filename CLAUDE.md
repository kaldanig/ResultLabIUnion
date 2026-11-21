# ResultUnion

A Result pattern library for .NET 11 built on C# 15 unions, with ASP.NET Core and gRPC integrations, analyzers,
benchmarks and demos. `README.md` describes the API, `docs/DESIGN.md` the decisions behind it.

## Writing code that uses ResultUnion

Before writing, changing or reviewing C# that uses ResultUnion (the demos, the samples, a test that exercises a
pipeline), read `.claude/skills/resultunion/SKILL.md` and the reference file it points to. The library's names and
conventions differ from other Result libraries (`Bind`, not `Then`; `Task` delegates, never `ValueTask` ones; error catalogs; who sets
`Field`), and code written from memory breaks them. Do this even when the task looks small.

Changes to the library itself (`ResultUnion/`, `ResultUnion.AspNetCore/`, `ResultUnion.Grpc/`,
`ResultUnion.FluentValidation/`, `ResultUnion.Http/`) follow
`docs/DESIGN.md`; record a new decision there when one is made.

## Build and test

```powershell
dotnet build ResultLabIUnion.slnx
```

```powershell
dotnet test --solution ResultLabIUnion.slnx
```

- Every `net11.0` project builds with .NET 11 Runtime Async (`Directory.Build.targets`). `-p:EnableRuntimeAsync=false`
  builds the classic state machines into `artifacts/state-machines/`; the tests must pass both ways (CI runs both).
- Coverage of the shipped assemblies: `docs/RELEASING.md`, "Code coverage".
- The ResultUnion analyzers (RU0001–RU0004) run on every project; fix their warnings rather than suppressing them.
  RU0004 is off for the test projects (`.editorconfig`); a deliberate read elsewhere gets a `#pragma` with the reason.
- The public API of each package is recorded in `PublicAPI.Shipped.txt` / `PublicAPI.Unshipped.txt`; a new or changed
  public member must be added to `PublicAPI.Unshipped.txt` (RS0016/RS0017), and user-visible changes go under
  `## [Unreleased]` in `CHANGELOG.md`. Releasing is described in `docs/RELEASING.md`.
- `dotnet pack` validates each package against the last release on nuget.org (`PackageValidationBaselineVersion`);
  a CP0002/CP0006 error is a binary breaking change. Avoid it, or, if it is intended, follow `docs/RELEASING.md`.
- The documentation site is DocFX (`docfx.json`, `docs/site/`); CI builds it with `--warningsAsErrors`, so a link in
  `README.md` or `docs/*.md` must point to another of those pages or to a full URL, never to a source file or folder.
  Check locally after a Release build: `dotnet tool restore; dotnet docfx docfx.json` (output in `artifacts/docs/_site`).
