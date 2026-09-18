---
title: Release workflow reference
description: Inputs, permissions and error responses of the Macro Deck plugin publishing workflow.
---

`Macro-Deck-App/GitHub-Actions/.github/workflows/publish-plugin.yml` builds, packs and uploads a plugin
to the Creator Portal. Call it as a reusable workflow; only builds from this workflow are accepted.

```yaml
jobs:
  publish:
    uses: Macro-Deck-App/GitHub-Actions/.github/workflows/publish-plugin.yml@v1
    permissions:
      contents: read
      id-token: write
    with:
      version: ${{ github.event.release.tag_name }}
      source: src/HelloDeck
```

## Permissions

| Permission | Why |
| --- | --- |
| `id-token: write` | Required. The upload signs in with the run's GitHub token. Without it the upload has no credential. |
| `contents: read` | Checks out the tagged commit. |

There is no secret to create. A step that uses a signing key or a publishing token is not part of this
workflow.

## Inputs

| Input | Required | Default | Description |
| --- | --- | --- | --- |
| `version` | yes | | Version of the build. One leading `v` is dropped (`v1.2.0` → `1.2.0`). Written into `manifest.json` and the assembly version. |
| `source` | yes | | Plugin project directory with `manifest.json` and `macrodeck-build.json`. |
| `build` | no | run number | Build identifier, 1-64 letters, digits, `.`, `-`, `_` or `+`. |
| `changelog` | no | empty | Default changelog of a release created from the build. |
| `build-per-platform` | no | `false` | Build each runtime identifier the manifest declares on a runner of its own platform and merge the packages. See [Building each platform on its own runner](#building-each-platform-on-its-own-runner). |
| `runners` | no | `{}` | JSON object naming the runner that builds a runtime identifier, merged over the defaults, for example `'{"osx-arm64": "macos-15"}'`. |
| `run-tests` | no | `true` | Run the repository's tests with `dotnet test -c Release` after the build. A failing test stops the release. |
| `test-path` | no | the only solution | Solution, project or directory `dotnet test` runs, relative to the repository root. With no `*.sln`/`*.slnx` at the root, or several, the step is skipped with a warning. |
| `run-stub-host` | no | `true` | Run the plugin on a disposable stub host with the conformance suite and upload its report. A failed required check stops the release. |
| `cli-version` | no | newest prerelease | `MacroDeck.Plugin.Cli` version to build with. |
| `upload-artifact` | no | `false` | Also keep the `.macroDeckPlugin` as a workflow artifact. |
| `artifact-name` | no | package file name | Name of that artifact. |
| `artifact-retention-days` | no | `0` | Days to keep the artifact, 1-90. `0` uses the repository default. |
| `platform-url` | no | `https://api.macro-deck.app` | Where the build is uploaded. |
| `audience` | no | `https://api.macro-deck.app` | Audience the Platform expects in the OIDC token. Changing it means the upload is refused. |

## Examples

Keep the package on the run:

```yaml
    with:
      version: ${{ github.event.release.tag_name }}
      source: src/HelloDeck
      upload-artifact: true
      artifact-retention-days: 7
```

## Building each platform on its own runner

By default one `ubuntu-latest` runner builds every runtime identifier the manifest declares: a .NET
plugin cross-builds from Linux, in one job and one restore. Turn the build into one job per platform
only when a target cannot be built that way - a `net10.0-windows` target, a native library compiled per
platform, or a build step needing Windows or macOS tooling:

```yaml
    with:
      version: ${{ github.event.release.tag_name }}
      source: src/HelloDeck
      build-per-platform: true
```

Each job then builds its own platform with [`build --rid`](/cli/build/), and a merge job combines them
with [`merge`](/cli/merge/) into the single package that is uploaded - the one a build on a machine
that could build every platform would have produced. Merging refuses packages that do not belong
together: a different plugin or version, a manifest differing beyond its `entrypoints`, a runtime
identifier twice, or a shared file whose bytes differ.

- The tests and the dependency list run once, on the first platform (Linux when the manifest declares
  it): they are about the repository, not about a runner.
- `upload-artifact` keeps the merged package, not the per-platform ones.
- The runner per platform defaults to `ubuntu-latest` (`linux-x64`), `windows-latest` (`win-x64`),
  `macos-latest` (`osx-arm64`), `ubuntu-24.04-arm` (`linux-arm64`), `windows-11-arm` (`win-arm64`) and
  `macos-15-intel` (`osx-x64`). `runners` replaces one, or names one for a platform not listed.
- One platform failing does not cancel the others, so a run shows every platform that is broken.

A rule of thumb: if `macrodeck-plugin build` succeeds on Ubuntu, leave this off.

## What gets uploaded

| File | Content |
| --- | --- |
| `.macroDeckPlugin` | The built, unsigned package. |
| `build-metadata.json` | Package id (from `manifest.json`), version, build and changelog. |
| `dependencies.json` | The NuGet packages the build restored, with known vulnerabilities. |
| `conformance.json` | Optional. The conformance suite's report from the stub host run; see [Conformance report](/creator-portal/conformance/). |

Commit, tag, repository and workflow are read from GitHub's signed token, never from these files.

## Errors

| Status | Cause | Fix |
| --- | --- | --- |
| `401` | No valid GitHub Actions token. | Add `id-token: write` to the job's permissions. |
| `403` | The build did not run through `publish-plugin.yml`. | Call the workflow with `uses:` instead of copying its steps. |
| `403` | Repository is not the Project's, or the package id is unknown. | Connect this repository to the Project and check the `id` in `manifest.json`. |
| `400` | The package or its metadata is unreadable. | Check the build step's log. |
| `409` | The run was not started from a tag. | Trigger the workflow with a published release. |
| `409` | The Project is in review. | Withdraw the submission and re-run. |
| `503` | GitHub's signing keys were unreachable. | Nothing was stored. Re-run the workflow. |
