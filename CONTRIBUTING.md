# Contributing to ResultUnion

Thank you for helping. Bug reports, questions, documentation fixes and code are all welcome.

- **A bug or an idea:** open an issue with one of the forms; they ask for what is needed to reproduce or judge it.
- **A security problem:** do not open an issue; report it privately as described in [SECURITY.md](SECURITY.md).
- **A larger change** (a new API, a new package, a different behaviour): open an issue first, so we can agree on the
  design before you spend time on code. The reasoning behind the existing API is in [docs/DESIGN.md](docs/DESIGN.md);
  a proposal that changes one of those decisions should say why.

## Building and testing

You need the .NET 11 SDK. Every `net11.0` project is built with .NET 11 Runtime Async, and the library must also work
with the classic async state machines, so run the tests both ways:

```powershell
dotnet build ResultLabIUnion.slnx
```

```powershell
dotnet test --solution ResultLabIUnion.slnx
```

```powershell
dotnet test --solution ResultLabIUnion.slnx -p:EnableRuntimeAsync=false
```

CI runs the same commands, plus code coverage, package validation, CodeQL and the documentation build, on every pull
request.

## What a pull request needs

The build must finish without warnings; several checks turn a mistake into a warning or an error on purpose.

- **Tests.** A fix comes with a test that fails without it; a new member comes with tests for its success path, its
  failure path, and its argument validation. Async members are tested with completed and pending tasks.
- **Analyzers.** The ResultUnion analyzers (RU0001–RU0008) run on every project, the library included. Fix what they
  report instead of suppressing it; a deliberate exception gets a `#pragma warning disable` with the reason.
- **Public API.** A new or changed public member must be listed in the package's `PublicAPI.Unshipped.txt` (the build
  reports RS0016/RS0017 until it is). Do not edit `PublicAPI.Shipped.txt`; it changes only when a version is released.
- **Compatibility.** `dotnet pack` compares each package with the last release and fails on a binary breaking change
  (CP0002, CP0006, …). Before 1.0 a break can be accepted, but only on purpose: see
  [docs/RELEASING.md](docs/RELEASING.md).
- **Changelog.** A change users will notice goes under `## [Unreleased]` in [CHANGELOG.md](CHANGELOG.md), in the
  section of the package it affects.
- **Documentation.** Public members have XML documentation (`<summary>`, and `<exception>` for what they throw); the
  packages generate their documentation file, so a missing comment is a CS1591 warning. A new
  feature is described in the README; a design choice someone might question is recorded in `docs/DESIGN.md`. Links in
  `README.md` and `docs/*.md` must point to another of those pages or to a full URL, since they are also built into the
  [documentation site](https://kaldanig.github.io/ResultLabIUnion/).
- **Articles.** The C# in `docs/articles/*.md` is compiled and run: every line must also appear in
  `ResultUnion.Articles` (the samples) or `ResultUnion.Articles.Tests` (the testing article). `ArticleCodeTests` fails
  when an article shows code that is not there, and checks the problem response Getting started shows against the
  real endpoint.

## Style

- Match the surrounding code: file-scoped namespaces, `var` where the type is obvious, expression bodies for one-line
  members, and a doc comment that says what a member does and what it returns on failure.
- Names follow the library's vocabulary: `Bind`, not `Then`; `Error` catalogs per domain concept; `Task` delegates, not
  `ValueTask` ones. [`.claude/skills/resultunion/SKILL.md`](.claude/skills/resultunion/SKILL.md) lists these
  conventions with the reason for each, and is worth reading before writing samples or tests.
- Commit messages: a short summary line in the imperative ("Add …", "Fix …"), then a body that explains why.

## License

By contributing, you agree that your contribution is licensed under the [MIT license](LICENSE) of this repository.
