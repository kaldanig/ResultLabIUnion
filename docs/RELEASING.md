# Releasing

The three packages (`ResultUnion`, `ResultUnion.AspNetCore`, `ResultUnion.Grpc`) are released together, with one version,
by pushing a tag. [`publish.yml`](../.github/workflows/publish.yml) builds, tests, packs and pushes them to nuget.org
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
2. **Version.** Set `<VersionPrefix>` in `Directory.Build.props` (for example `0.2.0`).
3. **Changelog.** In `CHANGELOG.md`, rename `## [Unreleased]` to `## [0.2.0] - YYYY-MM-DD`, add a new empty
   `## [Unreleased]` above it, and update the links at the bottom.
4. **Commit, merge to master, push.** Wait for CI to pass.
5. **Tag and push the tag:**

   ```powershell
   git tag v0.2.0
   ```

   ```powershell
   git push origin v0.2.0
   ```

`publish.yml` refuses a tag that does not match `VersionPrefix` or has no changelog entry. A prerelease is a tag with a
suffix on the same prefix: `v0.2.0-preview.1` publishes `0.2.0-preview.1`.

**A published version can never be reused**, even after unlisting it on nuget.org. If a release is wrong, fix it and
release the next version. Never delete and recreate a tag that has been published.

## Between releases

- Every public API change appears in `PublicAPI.Unshipped.txt` (the build warns with RS0016 or RS0017 until it is
  recorded), so a review sees it. Removing or changing a shipped member is a breaking change; before 1.0 it is allowed
  in a minor version and is listed in the changelog.
- Add user-visible changes under `## [Unreleased]` in `CHANGELOG.md` as they are merged.
