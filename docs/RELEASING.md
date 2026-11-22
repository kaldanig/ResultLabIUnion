# Releasing

The packages (`ResultUnion`, `ResultUnion.AspNetCore`, `ResultUnion.Grpc`, `ResultUnion.FluentValidation`, `ResultUnion.Http`, `ResultUnion.Testing`, `ResultUnion.Templates`) are released together, with one version,
by pushing a tag. [`publish.yml`](https://github.com/kaldanig/ResultLabIUnion/blob/master/.github/workflows/publish.yml) builds, tests, packs and pushes them to nuget.org
with NuGet trusted publishing: no API key is stored anywhere.

## One-time setup

1. **nuget.org policy.** Sign in at [nuget.org/account/trustedpublishing](https://www.nuget.org/account/trustedpublishing),
   choose **Add policy** and enter:
   - Repository Owner: `kaldanig`
   - Repository: `ResultLabIUnion`
   - Workflow File: `publish.yml` (the file name only, no path)
   - Environment: `release`
2. **GitHub environment.** In the repository, **Settings → Environments → New environment** named `release`, with the
   secret `NUGET_USER` set to your nuget.org profile name (not your email). Optionally add yourself as a **Required
   reviewer**, so every publish waits for your approval.

## Releasing a version

1. **API.** Move the lines from each package's `PublicAPI.Unshipped.txt` into its `PublicAPI.Shipped.txt`, keeping
   `#nullable enable` at the top of both files.
   Move the rules in `ResultUnion.Analyzers/AnalyzerReleases.Unshipped.md` into `AnalyzerReleases.Shipped.md` under
   `## Release 0.2.0`.
2. **Version.** Set `<VersionPrefix>` in `Directory.Build.props` (for example `0.2.0`).
3. **Changelog.** In `CHANGELOG.md`, rename `## [Unreleased]` to `## [0.2.0] - YYYY-MM-DD`, add a new empty
   `## [Unreleased]` above it, and update the links at the bottom. When the version breaks anything, open its section
   with "### Upgrading from 0.1.0": what to change in code that used the previous version.
4. **Commit, merge to master, push.** Wait for CI to pass: tag only a commit whose CI is green.
5. **Tag and push the tag:**

   ```powershell
   git tag v0.2.0
   ```

   ```powershell
   git push origin v0.2.0
   ```

   The tag starts `publish.yml`. Check its log: every package should say "Your package was pushed", none 403 or
   Conflict.
6. **GitHub Release.** When `publish.yml` has succeeded, create the release from the packages it pushed (the
   `packages-0.2.0` artifact of the run), with the version's changelog section as the text (links to other pages as
   full URLs, because relative ones do not resolve on the release page):

   ```powershell
   gh release create v0.2.0 --title "ResultUnion 0.2.0" --notes-file notes.md --verify-tag packages/*.nupkg packages/*.snupkg
   ```

   Add `--prerelease` for a release candidate.
7. **Baseline.** Set `<PackageValidationBaselineVersion>` in `Directory.Build.targets` to the version just released
   (for example `0.2.0`), delete any `CompatibilitySuppressions.xml`, and commit. From then on `dotnet pack` compares
   every change with that version. A package released for the first time (`<IsNewPackage>true</IsNewPackage>` in its
   project, as `ResultUnion.FluentValidation` had until 0.2.0) had no baseline until now: remove that property in the
   same commit.

   Package validation downloads the baseline from nuget.org, which lists a new version 10 to 30 minutes after the push.
   Push this commit only when every package shows it (`https://api.nuget.org/v3-flatcontainer/resultunion/index.json`
   and the same for each package), or CI fails to restore the baseline. Pack locally with
   `-p:RestoreNoHttpCache=true`: NuGet's HTTP cache keeps the old version list for up to 30 minutes and reports
   "Unable to find package" although the version is there.

The template package (`ResultUnion.Templates`) is not in the solution and has no API, so it has no PublicAPI files and
no package validation. When it is packed, `RESULTUNION_VERSION` in its template projects becomes the package version,
so a project created from it uses the ResultUnion packages of the same release; CI and `publish.yml` create projects
from it with those packages and build and test them (`ResultUnion.Templates/test-template.sh`) before anything is
pushed. Dependabot does not look inside the template (`exclude-paths`), so before a release update its other package
versions (`Microsoft.AspNetCore.OpenApi`, xUnit, …) to the ones the repository uses.

Before the first release of a new package, check the trusted publishing policy on nuget.org: its scope must allow
pushing *new* packages (not only new versions), with a glob pattern that matches the new ID (`*` or `ResultUnion*`).
Otherwise `publish.yml` pushes the existing packages and fails with 403 on the new one.

`publish.yml` refuses a tag that does not match `VersionPrefix` or has no changelog entry for exactly that version.

**A release candidate** is a tag with a suffix on the same prefix: with `VersionPrefix` `1.0.0`, the tag
`v1.0.0-rc.1` publishes `1.0.0-rc.1`, and the changelog needs a `## [1.0.0-rc.1] - YYYY-MM-DD` section. The steps are
the same, with two differences: the GitHub Release is marked `--prerelease`, and the baseline becomes the release
candidate (`1.0.0-rc.1`), so the final version can only add to it. The final release then gets its own section,
`## [1.0.0]`, which lists the changes since the last release candidate, or says there are none.

**A published version can never be reused**, even after unlisting it on nuget.org. If a release is wrong, fix it and
release the next version. Never delete and recreate a tag that has been published.

## Between releases

- Every public API change appears in `PublicAPI.Unshipped.txt` (the build warns with RS0016 or RS0017 until it is
  recorded), so a review sees it. Removing or changing a shipped member is a breaking change; before 1.0 it is allowed
  in a minor version and is listed in the changelog.
- Add user-visible changes under `## [Unreleased]` in `CHANGELOG.md` as they are merged.
- `dotnet pack` (run by CI on every push) validates each package against the last release and fails on a binary
  breaking change (CP0002, CP0006, …). If the break is intended, run
  `dotnet pack <project> -c Release -p:GenerateCompatibilitySuppressionFile=true`, commit the generated
  `CompatibilitySuppressions.xml`, and list the change in the changelog (DESIGN.md, 36).
- Dependabot (`.github/dependabot.yml`) opens pull requests every Monday for new NuGet package and GitHub Actions
  versions, with related packages grouped (Roslyn, ASP.NET Core, gRPC, OpenTelemetry, testing, actions). CI and Docs
  run on each one. Merge a green PR as it is; a dependency of a package (`Grpc.StatusProto`) changes what consumers
  get, so note it in the changelog.
- Benchmarks (`benchmarks.yml`): every change to the library runs each benchmark once, so a broken benchmark fails
  the run; the measured suite runs every Monday and on demand, with the reports in the run summary (docs/BENCHMARKS.md,
  "In CI"). Before a release, a look at the latest full run shows whether an allocation appeared on a success path.
- Security: CodeQL (`codeql.yml`) scans the C# code and the workflows on every push, pull request and weekly;
  findings are under **Security → Code scanning**. Dependabot alerts and security updates, secret scanning with push
  protection, and private vulnerability reporting are enabled in the repository settings. A reported vulnerability is
  fixed in a new version of all the packages and announced as a GitHub security advisory (`SECURITY.md`).

## Documentation site

The site (the README as the guide, the pages in `docs/`, and an API reference generated from the compiled packages) is
built with [DocFX](https://dotnet.github.io/docfx/) by [`docs.yml`](https://github.com/kaldanig/ResultLabIUnion/blob/master/.github/workflows/docs.yml)
on every push and pull request; a broken link or cross reference fails that build. It describes `master`, not the
latest release.

To publish it on GitHub Pages (once): **Settings → Pages → Source: GitHub Actions**, then **Settings → Secrets and
variables → Actions → Variables** and add `PAGES_ENABLED` = `true`. Every push to master then deploys the site. Pages
on a private repository needs a paid GitHub plan; without `PAGES_ENABLED` the site is still built and checked, just not
deployed.

Build and view it locally (the API reference is read from the Release build):

```powershell
dotnet build ResultLabIUnion.slnx -c Release
```

```powershell
dotnet tool restore; dotnet docfx docfx.json --serve
```

The site is written to `artifacts/docs/_site` and served on <http://localhost:8080>.

## Code coverage

CI measures line and branch coverage of the shipped assemblies (`ResultUnion`, `ResultUnion.AspNetCore`,
`ResultUnion.Grpc`, the analyzers and the code fixes; `codecoverage.settings.xml`) while the tests run with Runtime Async.
The summary is on the run page; the full HTML report is the run's `coverage` artifact. To produce it locally:

```powershell
dotnet test --solution ResultLabIUnion.slnx -c Release --coverage --coverage-settings codecoverage.settings.xml --coverage-output-format cobertura --results-directory artifacts/coverage
```

```powershell
dotnet tool restore; dotnet reportgenerator -reports:'artifacts/coverage/*.cobertura.xml' -targetdir:artifacts/coverage/report -reporttypes:Html
```

Then open `artifacts/coverage/report/index.html`. There is no coverage threshold: the number shows where tests are
missing, not whether a change may be merged.

## Mutation testing

Code coverage says which lines the tests run; mutation testing says whether they would notice a bug there. Stryker.NET
(a local tool, configured in `stryker-config.json`) makes small changes to a package, such as removing an argument
check or flipping a condition, and runs that package's tests against each one. A change no test fails on (a
*survivor*) usually means a missing assertion.

`mutation.yml` runs it for every package every Monday and on demand, and uploads the HTML reports as artifacts;
it never blocks a pull request. To run it locally for one package:

```powershell
cd ResultUnion.Tests; dotnet tool restore; dotnet stryker --config-file ../stryker-config.json --project ResultUnion.csproj --output ../artifacts/stryker/ResultUnion
```

The report is `artifacts/stryker/ResultUnion/reports/mutation-report.html`. Notes for reading it:

- The tests run on Microsoft.Testing.Platform, so the configuration selects Stryker's `mtp` test runner, which Stryker
  still marks as a preview; the default VSTest runner reports every mutant as surviving with this setup.
- `ConfigureAwait(false)` → `true` is ignored: without a synchronization context the two behave the same.
- A negated `IsCompletedSuccessfully`, or the removal of the block it guards, sends a completed task down the general
  path, which returns the same result, so those survivors are expected unless a test checks that the source task is
  reused.
- Mutants marked *static* (in `static readonly` fields: the JSON property names, `Error.DefaultResult`, the meter's
  description) cannot be switched on per test, because the field is initialized once per test process; they show as
  surviving although the tests check those values exactly.
- Not every survivor is a missing test. Before writing one, apply the mutant by hand and run the tests that cover it
  (the report lists them): the survivors at the failure message of `ShouldHaveFieldErrors` were killed that way by the
  test that compares the message. Changes to `GetHashCode` constants, to exception messages and to checks that the
  code repeats later (a `NaN` test before a range test that excludes `NaN` anyway) are expected survivors too.
- Stryker leaves its mutated build in the test project's `bin` folder; run `dotnet build --no-incremental` before testing
  again, or the next `dotnet test --no-build` runs against a mutant.
- The argument checks are covered by one contract test per package (`NullArgumentTests`, built on
  `ResultUnion.Tests/Contracts/NullArgumentContract.cs`): it calls every public method with each non-nullable argument
  set to `null`, once with completed and once with pending tasks, and expects an `ArgumentNullException` at once.
