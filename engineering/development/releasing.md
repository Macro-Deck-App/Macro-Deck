# Releasing and Packaging

The release implementation lives in [`.github/workflows/build.yml`](../../.github/workflows/build.yml). This page documents how to operate it and the constraints that are easy to miss. Do not mirror the workflow job graph here.

## Start a release

Run the `Build` workflow manually and provide `version`: a stable version such as `3.0.0` or a beta such as `3.0.0-beta.4`. Version validation and native package version mapping are handled by [`ci/scripts/release-version.mjs`](../../ci/scripts/release-version.mjs).

Every release covers Windows, Linux, and macOS. There is no platform selection, and nothing reaches the release feed unless all three packaged successfully and the end-to-end suite passed - a channel file that advertises a version its platform never built would offer installed clients an update that 404s.

Release artifacts always use the Production build identity. Beta is derived from the version; it is not a separate build channel.

## What the workflow does

The workflow tests every platform, builds the Angular UI, publishes the self-contained host for all three runtime identifiers, packages the Tauri application on the target operating systems, signs where credentials are available, runs the end-to-end suite against a production host staged from the same revision, and uploads release artifacts/update metadata once all of that has finished.

The Tauri bootstrapper is the installed entry point. The published host and Angular output are staged into its application bundle before packaging.

Relevant implementation files:

- [`.github/workflows/build.yml`](../../.github/workflows/build.yml) - release orchestration.
- [`ci/scripts/stage-host.sh`](../../ci/scripts/stage-host.sh) - local host staging for the bootstrapper.
- [`ci/scripts/release-version.mjs`](../../ci/scripts/release-version.mjs) - release version validation/mapping.
- [`ci/scripts/make-update-manifest.mjs`](../../ci/scripts/make-update-manifest.mjs) - updater metadata.
- [`ui/bootstrapper/tauri.conf.json`](../../ui/bootstrapper/tauri.conf.json) and platform overrides - package configuration.

These files are the source of truth for exact job dependencies, artifact names, runner versions, command flags, and bundle layout.

## Signing and publishing

Official packages may require platform signing/notarization, updater signing, and release storage credentials. The workflow owns the exact secret names and guards.

A release should not be considered production-ready merely because an unsigned contributor build succeeded. Check the package/signing steps for every platform and confirm the expected artifacts reached the release feed.

## Update channels

Stable and beta updater metadata are generated from the normalized release version. Do not manually invent channel file contents. When update behaviour changes, update the updater implementation and manifest-generation script together.

Publishing a release to a feed does not mean every install downloads it automatically: whether a periodic check downloads a new release, only asks first, or never runs at all now depends on the user's own auto-update mode (see [ADR 0015](../decisions/0015-installation-and-update-delivery.md)).

## NuGet packages

The SDK/plugin package family is packed from the release revision and version. The reusable workflows are:

- [`.github/workflows/nuget-pack.yml`](../../.github/workflows/nuget-pack.yml)
- [`.github/workflows/nuget-publish.yml`](../../.github/workflows/nuget-publish.yml)
- [`.github/workflows/nuget.yml`](../../.github/workflows/nuget.yml)

Package validation is implemented in [`ci/scripts/verify-nuget-packages.mjs`](../../ci/scripts/verify-nuget-packages.mjs).

Each package project owns a `README.md` that is packed as its NuGet readme and displayed on nuget.org. These files are package assets. Do not remove, merge, or move them as part of general documentation cleanup unless the package metadata is changed at the same time and the resulting `.nupkg` is verified.

## Local release-like packaging

Build the Angular production output first, then stage the host for the required RID:

```bash
cd ui/angular
npm ci
npm run build:prod
cd ../..

ci/scripts/stage-host.sh <rid> <version> Production
```

Then use the bootstrapper packaging command appropriate for the current operating system from `ui/bootstrapper/`.

Local packaging is useful for verifying bundle shape. It does not replace official signing/notarization or the release workflow.

## Before publishing

Confirm the release version is correct, all intended platform jobs succeeded, tests passed, expected signed packages exist, updater metadata points at the correct artifacts, and NuGet packages were produced from the intended revision/version.

If a release step changes, prefer updating the workflow/script itself and keep this page limited to operator-facing behaviour and non-obvious constraints.