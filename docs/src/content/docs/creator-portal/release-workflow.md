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
| `cli-version` | no | newest prerelease | `MacroDeck.Plugin.Cli` version to build with. |
| `upload-artifact` | no | `false` | Also keep the `.macroDeckPlugin` as a workflow artifact. |
| `artifact-name` | no | package file name | Name of that artifact. |
| `artifact-retention-days` | no | `0` | Days to keep the artifact, 1-90. `0` uses the repository default. |

## Examples

Keep the package on the run:

```yaml
    with:
      version: ${{ github.event.release.tag_name }}
      source: src/HelloDeck
      upload-artifact: true
      artifact-retention-days: 7
```

## What gets uploaded

| File | Content |
| --- | --- |
| `.macroDeckPlugin` | The built, unsigned package. |
| `build-metadata.json` | Package id (from `manifest.json`), version, build and changelog. |
| `dependencies.json` | Optional. The NuGet packages the build restored, with known vulnerabilities. |

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
