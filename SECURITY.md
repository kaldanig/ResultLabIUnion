# Security policy

## Supported versions

ResultUnion is before 1.0. Security fixes are released as a new version of all three packages (`ResultUnion`,
`ResultUnion.AspNetCore`, `ResultUnion.Grpc`) on top of the latest release; older versions are not patched.

| Version | Supported |
|---|---|
| Latest 0.x release | Yes |
| Earlier releases | No |

## Reporting a vulnerability

Please do not open a public issue for a security problem.

Report it privately through GitHub: on the repository page, **Security → Report a vulnerability**
([direct link](https://github.com/kaldanig/ResultLabIUnion/security/advisories/new)). Include what you found, the
affected package and version, and if possible a small program or request that shows it.

You will get an answer within a week. Once the problem is confirmed, a fixed version is published on nuget.org and a
GitHub security advisory describes the issue, credits you unless you prefer otherwise, and tells users which version to
upgrade to.

## What counts

Examples of what is in scope:

- an `Unexpected` error whose message or exception details reach an HTTP problem response or a gRPC status, although
  the integrations promise to hide them;
- error messages or arguments written to OpenTelemetry metrics or spans, which are meant to carry only codes and
  categories;
- a crafted HTTP request, gRPC status or JSON error that makes `ToError` or `ErrorJsonConverter` throw unexpectedly,
  allocate without bound, or produce an error of a different type;
- a vulnerable dependency shipped in one of the packages.

The demo applications (`ResultUnion.WebApi`, `ResultUnion.GrpcService`, …) are samples, not hardened services; a
problem in them is welcome as an ordinary issue unless it also affects the packages.
