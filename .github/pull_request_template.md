## What and why

<!-- What does this change, and why? Link the issue it resolves ("Fixes #123"). -->

## Checklist

- [ ] Tests pass with Runtime Async and with `-p:EnableRuntimeAsync=false`
- [ ] New behaviour is covered by tests (success, failure, argument validation)
- [ ] The build has no warnings (analyzers, RS0016/RS0017, CS1591)
- [ ] Public API changes are in `PublicAPI.Unshipped.txt`; `dotnet pack` reports no unintended breaking change
- [ ] User-visible changes are under `## [Unreleased]` in `CHANGELOG.md`
- [ ] README or `docs/DESIGN.md` updated where the change needs it

See CONTRIBUTING.md for details.
