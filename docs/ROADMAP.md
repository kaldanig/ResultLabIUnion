# Roadmap and versioning

What is left before 1.0, and what 1.x promises once it is out. The decisions behind the API are in
[DESIGN.md](DESIGN.md); what changed in each version is in the [changelog](../CHANGELOG.md).

## Where things stand

1.0.0-rc.1 is the first release candidate: 0.5.0 without the members it deprecated. It has the full set of packages
(ResultUnion with its analyzers and code fixes, ResultUnion.AspNetCore, ResultUnion.Grpc, ResultUnion.FluentValidation,
ResultUnion.Http, ResultUnion.Testing and ResultUnion.Templates) and the outcome of the API review before 1.0
([DESIGN.md, 43](DESIGN.md#43-the-api-review-before-10)). Every package depends only on stable releases: .NET 11,
Grpc.StatusProto and FluentValidation. Runtime Async, which the libraries are compiled with, is a supported .NET 11
feature, no longer a preview.

1.0.0-rc.2 has the same API as rc.1. It adds the item templates (`resultunion-valueobject`, `resultunion-feature`,
`resultunion-feature-tests`), `--database sqlite` for `resultunion-api` and a code fix for RU0005, so that they are
tried before 1.0.0.

1.0.0-rc.3 has the same API again. It adds `resultunion-grpc` (with `--database sqlite`) and `--grpc` for the feature
templates; 1.0.0 follows when it has been out for four weeks.

Before 1.0 a minor version may contain breaking changes; each one is listed under **Changed** or **Removed** in the
changelog, with an "Upgrading from" section for the version before.

## Before 1.0

1. ~~**Remove the deprecated members.**~~ Done in 1.0.0-rc.1: `ErrorType.ToStatusCode()` (now `ToHttpStatusCode()`)
   and `GetValueOrThrowRpc(onSuccess)` / `GetValueOrThrowRpcAsync(onSuccess)` on a result without a value (now
   `ToReplyOrThrowRpc` / `ToReplyOrThrowRpcAsync`).
2. ~~**Freeze the analyzer rules.**~~ Done: RU0001–RU0010 keep their IDs and severities in 1.x. RU0001–RU0005,
   RU0009 and RU0010 are warnings, RU0006–RU0008 suggestions (Info). An ID is never reused for another rule.
3. ~~**Write the compatibility promise into the documentation**~~. Done: this page's "Versioning in 1.x" section,
   linked from the README and CONTRIBUTING, and the release checklist in RELEASING.md.
4. ~~**Release 1.0.0-rc.1**~~ with the deprecated members removed. Done on 2026-11-22, followed by 1.0.0-rc.2 the
   same day and 1.0.0-rc.3 (the gRPC templates) on 2026-11-23. The last candidate stays out for at least four weeks;
   issues that need an API change go into a further release candidate.
5. **Release 1.0.0** from the last release candidate when no issue needs an API change. The package validation
   baseline then becomes 1.0.0, and from that release on, a CP error in `dotnet pack` means the change waits for 2.0.

### Ready when

- every package builds and its tests pass in both async modes, and the template creates projects that build and pass
  their tests (CI checks all three on every push);
- the weekly mutation run shows no gap that a test should cover (survivors are expected changes, as RELEASING.md
  describes);
- the documentation site builds without warnings, and every public member has XML documentation;
- a release candidate has been out for four weeks without an issue that needs an API change.

## Versioning in 1.x

From 1.0.0 on, versions follow [Semantic Versioning](https://semver.org/) strictly: a 1.x minor or patch version never
requires a change to code that compiled and worked with an earlier 1.x version.

### What 1.x keeps

- **The public C# API** of every package, as recorded in its `PublicAPI.Shipped.txt`. `dotnet pack` checks binary
  compatibility against the previous release (package validation), so a removed or changed member fails the build.
- **Documented behavior**: what a method returns or throws, the order a pipeline runs its steps in, that cancellation
  is never caught, that a default result is a failure carrying `Error.DefaultResult`, and that a result refuses to be
  serialized.
- **The wire formats**, because other systems read them:
  - the JSON format of `Error` (property names, how `type` and arguments are written);
  - the error codes the library creates: those of the `Error` factories (`validation.failed`, `resource.not_found`,
    `conflict`, `concurrency.conflict`, `business_rule.violated`, `unauthorized`, `forbidden`, `rate_limit.exceeded`,
    `dependency.unavailable`, `unexpected`), `result.default`, the `http.*` codes of ResultUnion.Http and the `grpc.*`
    codes of ResultUnion.Grpc;
  - the HTTP status for each `ErrorType` and the problem details body (`code`, `category`, `arguments`, `errors`,
    `details`);
  - the gRPC status code for each `ErrorType` and the `google.rpc` details (`ErrorInfo` reason and metadata keys,
    `BadRequest`, `RetryInfo`);
  - the OpenTelemetry meter, counter, event and attribute names.

  New members may be added to these formats (a new JSON property, a new metadata key); readers must ignore what they do
  not know, as ResultUnion's own readers do.
- **The analyzer rules**: IDs and severities. A new rule arrives as a suggestion (Info) in a minor version, so an
  upgrade never fails a build that treats warnings as errors; it can become a warning only in the next major version.
  A rule may find more cases or fewer false positives in a minor version.
- **The target framework**: .NET 11. A later .NET is added as a target in a minor version; dropping .NET 11 waits for a
  major version.

### What may change in a minor version

- new types, members, overloads, packages and analyzer rules (as suggestions);
- new members in the wire formats, as above;
- messages: exception messages, analyzer messages and the default messages of the `Error` factories;
- performance and allocations, and internals that are not documented behavior;
- a member may be marked `[Obsolete]`, with the replacement in its message; it is removed only in the next major
  version.

### What waits for 2.0

Anything in "What 1.x keeps": removing or changing a public member, changing documented behavior or an existing part of
a wire format, raising an analyzer rule from suggestion to warning, and dropping a target framework.
