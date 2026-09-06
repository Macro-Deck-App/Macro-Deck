---
title: macrodeck-plugin build
description: Build every runtime identifier the manifest declares, stage them into one payload, and package the result.
---

Builds every runtime identifier your manifest declares, stages them into one payload, and packages the
result with the same implementation [`pack`](/cli/pack/) uses. Where `pack` starts from output you already
produced, `build` produces it:

```bash
macrodeck-plugin build
```

| Option | Default | What it does |
| --- | --- | --- |
| `--source <dir>` | `.` | The plugin project directory. |
| `--manifest <path>` | `<source>/manifest.json` | The manifest that decides which runtime identifiers to build. |
| `--build-config <path>` | `macrodeck-build.json` beside the manifest | The build recipe. |
| `--rid <rid>` | all declared | Build one declared runtime identifier only. |
| `--output <dir>` | `.` | **A directory**, unlike `pack --output`, which is a file path - the artifact's name is derived from the plugin id and version so a build always lands somewhere predictable. |
| `--force` | off | Overwrite an existing artifact. |

`manifest.entrypoints` is the source of truth for *what* to build and `macrodeck-build.json` for *how*. A
runtime identifier declared in the manifest with no matching target fails the build - a declared platform is
never silently skipped. The build runs each target in turn, launching its `executable` with its `arguments`
as a vector; nothing is passed through a shell, so an argument containing spaces, quotes or `$HOME` reaches
the tool exactly as written.

## Staging layout

Each target's output is staged under the directory its own entrypoint declares, which is what keeps a macOS
and a Linux build - identically named executables - from overwriting each other:

```text
manifest.json
runtimes/win-x64/MyPlugin.exe
runtimes/osx-arm64/MyPlugin
runtimes/linux-x64/MyPlugin
assets/icon.png
```

Everything else beside `manifest.json` is copied once into the package root, so a manifest referring to
`assets/icon.png` resolves. `macrodeck-build.json`, each target's configured `output` directory, and
`bin/`, `obj/`, `.git/`, `.vs/`, `.idea/`, `node_modules/` and `.DS_Store` are excluded - **everything else
is packaged, including your sources**, so keep anything you do not want to distribute out of the directory
holding the manifest. Staging happens in a temporary directory outside your project and is removed when the
command finishes.

## Every requested runtime identifier must produce its entrypoint

Where `pack` only warns about a declared entrypoint it cannot find, `build` fails. This is what catches a
manifest declaring a Windows `.exe` while the configured build actually produces a framework-dependent
`.dll`. A full build gates every declared runtime identifier; `macrodeck-plugin build --rid win-x64` gates
only `win-x64`, so a CI matrix job is not failed by the platforms it was never asked to build.

A `--rid` build writes `<id>-<version>-<rid>.macroDeckPlugin`, and the manifest inside it declares only
that runtime identifier - an honest single-platform package rather than one claiming platforms that job
never produced. Nothing in the CLI merges those per-runner artifacts back into one multi-platform package;
for a single artifact covering every platform, run a full build on a machine that can build them all.

The CLI does not model which platforms a machine can build. Every requested target is attempted, and a
toolchain that is missing or fails is reported with the runtime identifier that was being built and the
tool's own stdout and stderr.

**`build` never signs.** It produces an unsigned package, needs no key, and drops any `signature` and
`files[]` the project manifest happened to carry - `files[]` is recomputed from the staged bytes, so a
signature made before the build could not describe the artifact anyway. Signing is
[`sign`](/cli/signing/#sign), or the Creator Portal.

**`build` fills in `languages`.** The manifest's
[`languages`](/reference/manifest/#languages) list is derived from the project's `Localization/*.resx`
set - an unsuffixed `Strings.resx` counts as `en` and each culture-suffixed sibling contributes its own
BCP-47 tag - so what a store shows before installing matches the catalog the plugin serves once it runs.
`build` is where this works best, because it still has the project tree; the staged payload it hands to
`pack` no longer does. See [the localization guide](/sdk/localization/#the-manifest-languages-field).

**`build` also warns about publication readiness.** It evaluates the manifest at the Publication
[requirement level](/reference/manifest/#requirement-categories) and reports every unsatisfied
`publication` field (e.g. a missing `description` or `publisher`) as a `publication-metadata-missing`
warning - this never fails the build; a publication-incomplete but structurally sound plugin still
builds successfully. `build` takes no `--level` flag; this check always runs.

Note that `pack`'s `source-looks-like-debug-build` warning cannot fire here, because the payload `build`
packs is a temporary directory. Whether a Release or a Debug build is produced is entirely up to the
recipe, which is why the generated one publishes Release.

Exit code is `Success` (0), `UsageError` (2) for a `--rid` the manifest does not declare or an artifact
that already exists without `--force`, `InputUnreadable` (3) when the manifest, the build configuration or
the build tool itself could not be found, `SubjectInvalid` (1) when a build failed or the result does not
match what the manifest declares, and `Cancelled` (4) on Ctrl-C.
