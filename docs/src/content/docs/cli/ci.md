---
title: CI and automation
description: 'A GitHub Actions workflow that builds, validates and conformance-tests a plugin on every platform, gated on exit codes.'
---

Every command reports its verdict through an [exit code](/cli/#exit-codes), and `build`, `validate` and
`test` need no Macro Deck installation and no credentials, so the CLI drops straight into a pipeline.

## One runner, or one per platform

`build` without `--rid` builds every runtime identifier the manifest declares, in one job and one
restore. A .NET plugin cross-builds from Linux, so for most plugins one `ubuntu-latest` runner is the
whole build:

```bash
macrodeck-plugin build --source src/HelloDeck --output ./artifacts
```

```text
Building linux-x64...
Building osx-arm64...
Building win-x64...
Built linux-x64, osx-arm64, win-x64.
```

Split the build across runners only when a target cannot be built from Linux: a `net10.0-windows`
target, a native library compiled per platform, or a build step that needs Windows or macOS tooling.
Then each runner builds its own platform with `--rid` and [`merge`](/cli/merge/) puts the packages back
together. The matrix below is that case; if a plain `build` succeeds on Ubuntu, you do not need it.

If you publish through [`publish-plugin.yml`](/creator-portal/release-workflow/), do not write this
matrix yourself - set `build-per-platform: true` and the workflow splits and merges for you.

## GitHub Actions

Save as `.github/workflows/plugin.yml` in a project created with [`macrodeck-plugin new`](/cli/new/)
(here `HelloDeck`; replace it with your project name). It builds each platform on its own runner, so
use it for a plugin that needs that; otherwise drop the matrix and the `--rid`, and build every
platform in the one job.

```yaml
name: Plugin

on:
  push:
    branches: [main]
  pull_request:

jobs:
  build:
    strategy:
      fail-fast: false
      matrix:
        include:
          - os: windows-latest
            rid: win-x64
          - os: macos-latest
            rid: osx-arm64
          - os: ubuntu-latest
            rid: linux-x64
    runs-on: ${{ matrix.os }}
    defaults:
      run:
        shell: bash
    steps:
      - uses: actions/checkout@v4

      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 10.0.x

      - name: Install the CLI
        run: dotnet tool install --global MacroDeck.Plugin.Cli --version 3.0.0-beta.11

      - name: Unit tests
        run: dotnet test

      - name: Build the artifact
        run: macrodeck-plugin build --source src/HelloDeck --rid ${{ matrix.rid }} --output ./artifacts

      - name: Validate for publication
        run: macrodeck-plugin validate --level publication --artifact ./artifacts/*.macroDeckPlugin

      - name: Conformance
        run: |
          macrodeck-plugin test --artifact ./artifacts/*.macroDeckPlugin \
            --report markdown --output conformance.md
          cat conformance.md >> "$GITHUB_STEP_SUMMARY"

      - uses: actions/upload-artifact@v4
        with:
          name: plugin-${{ matrix.rid }}
          path: artifacts/*.macroDeckPlugin
```

Each step fails the job on a non-zero exit code; no output parsing is needed.

To publish one package for every platform, add a job that merges what the matrix built:

```yaml
  package:
    needs: build
    runs-on: ubuntu-latest
    steps:
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 10.0.x

      - name: Install the CLI
        run: dotnet tool install --global MacroDeck.Plugin.Cli --version 3.0.0-beta.11

      - uses: actions/download-artifact@v4
        with:
          pattern: plugin-*
          path: artifacts
          merge-multiple: true

      - name: Merge
        run: macrodeck-plugin merge artifacts/*.macroDeckPlugin --output dist

      - uses: actions/upload-artifact@v4
        with:
          name: plugin
          path: dist/*.macroDeckPlugin
```

## Notes

- **One runtime identifier per runner.** `build --rid` builds only that target, so each job produces and
  tests its own single-platform artifact. The RIDs must be ones your `manifest.json` declares.
- **`test` launches the plugin**, so it tests the artifact whose entrypoint matches the runner's own
  platform. It needs the ASP.NET Core shared framework, which the .NET SDK includes.
- **`validate --level publication`** fails (exit 1) on missing Store metadata such as `repository`; drop
  the step until you are ready to publish. See [Publishing to the Store](/guides/publishing/).
- **Pin the version.** `--prerelease` resolves to `3.0.0-preview.10`: SemVer orders `beta` before
  `preview`, so the newest published release, `3.0.0-beta.11`, is not the one it picks - and
  `preview.10` has no `merge`. Install with `--version` until a stable 3.0 CLI ships.
- **Never put a signing key or certificate in CI.** A publishing workflow submits an unsigned artifact;
  the Creator Portal signs it.
- **Tell a broken plugin from a broken runner:** exit 1 means the plugin is wrong, exit 3 means an input
  could not be read or launched.

## Verifying a signed artifact

```yaml
      - name: Verify signature
        run: macrodeck-plugin verify ./downloads/com.example.hello-deck-1.0.0-linux-x64.macroDeckPlugin --output json
```

- [`verify`](/cli/signing/#verify) is the only signing command a plugin's CI needs.
- **It needs no network.** The Macro Deck root public key is compiled in, so it runs offline and in
  air-gapped runners.
- Gate on the exit code. `--output json` gives the same verdict as
  `{ valid, format, certificateId, rootAnchored, revocationChecked, problems[] }` when you need details.
- Every run states that revocation was not checked. A `valid` verdict is a cryptographic fact about the
  signature and chain at signing time, not a live trust decision; nothing in the CLI consults
  `security.json` or any other revocation feed.
- `--root-public` lets a test pipeline use a throwaway root instead of the pinned one. Never use it to
  verify a production release; it prints `warning non-production-root`.

These points apply equally to [`sign`](/cli/signing/#sign), wherever it does run.

## See also

- [`macrodeck-plugin build`](/cli/build/) - the `--rid` matrix build.
- [`macrodeck-plugin merge`](/cli/merge/) - one package from the matrix.
- [Release workflow reference](/creator-portal/release-workflow/) - `build-per-platform` does all of
  this inside the publishing workflow.
- [`macrodeck-plugin test`](/cli/test/) - filters and report formats.
- [Signing packages](/cli/signing/) - `verify` in full.
- [Publishing to the Store](/guides/publishing/) - what the publishing workflow submits.
