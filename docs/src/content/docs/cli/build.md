---
title: macrodeck-plugin build
description: Build every runtime identifier the manifest declares, stage them into one payload, and package the result.
---

`build` builds every runtime identifier your manifest declares and packages the result into one
`.macroDeckPlugin`, using the same packer as [`pack`](/cli/pack/).

## Examples

```bash
cd ~/src/SpotifyController/src/SpotifyController
macrodeck-plugin build --output ../../artifacts
```

```text
Building linux-x64...
Building osx-arm64...
Building win-x64...
Built linux-x64, osx-arm64, win-x64.
Packed com.example.spotify-controller 1.0.0 -> ../../artifacts/com.example.spotify-controller-1.0.0.macroDeckPlugin (1041 entries, 342566393 bytes uncompressed).
```

A full multi-platform package. Run it from the directory holding `manifest.json`.

```bash
macrodeck-plugin build --rid osx-arm64 --output ../../artifacts
```

```text
Building osx-arm64...
Built osx-arm64.
Packed com.example.spotify-controller 1.0.0 -> ../../artifacts/com.example.spotify-controller-1.0.0-osx-arm64.macroDeckPlugin (...).
```

One platform only. Use it in a CI matrix job.

```bash
macrodeck-plugin build --source src/SpotifyController --output artifacts --force
```

Build from the repository root and overwrite the previous artifact.

## Options

| Option | Default | Description |
| --- | --- | --- |
| `--source <dir>` | `.` | The plugin project directory. |
| `--manifest <path>` | `<source>/manifest.json` | The manifest that decides which runtime identifiers to build. |
| `--build-config <path>` | `macrodeck-build.json` beside the manifest | The build recipe. |
| `--rid <rid>` | all declared | Build only this runtime identifier, which the manifest must declare. |
| `--output <dir>` | `.` | **Directory** for the artifact, unlike `pack --output`, which is a file path. |
| `--force` | off | Overwrite an existing artifact. |

The artifact is named `<id>-<version>.macroDeckPlugin`, or `<id>-<version>-<rid>.macroDeckPlugin` with
`--rid`.

## What gets built

`manifest.entrypoints` decides what to build; `macrodeck-build.json` decides how.

- A runtime identifier declared in the manifest without a matching target fails with
  `target-not-configured` - a declared platform is never skipped.
- Each target runs in turn: its `executable` with its `arguments` as a vector, never through a shell, so
  spaces, quotes or `$HOME` reach the tool exactly as written.
- The CLI does not know which platforms a machine can build. Every requested target is attempted, and a
  missing or failing toolchain is reported with the runtime identifier and the tool's stdout and stderr.
- Whether the output is Release or Debug is up to the recipe; the generated one publishes Release. (`pack`'s
  `source-looks-like-debug-build` warning cannot fire here, because `build` packs a temporary directory.)

## Staging layout

```text
manifest.json
runtimes/win-x64/MyPlugin.exe
runtimes/osx-arm64/MyPlugin
runtimes/linux-x64/MyPlugin
assets/icon.png
```

- Each target's output is staged under the directory its entrypoint declares, so identically named macOS
  and Linux executables do not overwrite each other.
- Everything else beside `manifest.json` is copied once into the package root, so a manifest referring to
  `assets/icon.png` resolves.
- Excluded: `macrodeck-build.json`, each target's `output` directory, the `--output` directory (unless it is
  the project directory itself), `bin/`, `obj/`, `.git/`, `.vs/`, `.idea/`, `node_modules/`, `.DS_Store`
  and any `.macroDeckPlugin` file. **Everything else is packaged, including your sources**, so keep anything
  you do not want to distribute out of that directory.
- An `--output` directory inside the project, such as `.` or `./artifacts`, is safe to build into repeatedly.
- Staging happens in a temporary directory outside your project, removed when the command finishes.

## Entrypoint checks

Every requested runtime identifier must produce its declared entrypoint, or the build fails with
`entrypoint-missing` (where `pack` only warns). This catches a manifest declaring a Windows `.exe` while the
recipe produces a framework-dependent `.dll`.

A full build checks every declared runtime identifier; `--rid win-x64` checks only `win-x64`, so a matrix
job is not failed by platforms it never built.

## Single-platform artifacts

A `--rid` artifact's manifest declares only that runtime identifier, so it never claims platforms the job
did not produce. The CLI does not merge per-runner artifacts; for one package covering every platform, run
a full build on a machine that can build them all.

## What build changes in the manifest

- **Never signs.** The package is unsigned, needs no key, and any `signature` and `files[]` in the project
  manifest are dropped. `files[]` is recomputed from the staged bytes. Signing is
  [`sign`](/cli/signing/#sign) or the Creator Portal.
- **Fills in `languages`.** [`languages`](/reference/manifest/#languages) is derived from
  `Localization/*.resx`: an unsuffixed `Strings.resx` counts as `en`, and each culture-suffixed sibling adds
  its BCP-47 tag. `pack` cannot do this, because its payload no longer contains the project tree. See
  [the localization guide](/features/localization/#the-manifest-languages-field).
- **Warns about publication readiness.** Every unsatisfied field at the `publication`
  [requirement level](/reference/manifest/#requirement-categories) (for example a missing `description` or
  `publisher`) is a `publication-metadata-missing` warning. It never fails the build, and there is no
  `--level` flag to turn it off.

## Exit codes

```text
$ macrodeck-plugin build --rid win-arm64
error rid-not-declared: The manifest does not declare 'win-arm64'. Declared runtime identifiers: linux-x64, osx-arm64, win-x64.
```

| Code | When |
| --- | --- |
| 0 | The artifact was written. |
| 1 | A build failed, or the manifest, build configuration or result does not match what the manifest declares. |
| 2 | A `--rid` the manifest does not declare, or an artifact that already exists without `--force`. |
| 3 | The source, the manifest, the build configuration or the build tool could not be found. |
| 4 | Cancelled (Ctrl-C). |
| 70 | Staging failed. |

## See also

- [`new`](/cli/new/) - scaffold a project with a ready `macrodeck-build.json`.
- [`pack`](/cli/pack/) - package output you built yourself.
- [`validate`](/cli/validate/) - check the artifact `build` produced.
- [Manifest reference](/reference/manifest/).
