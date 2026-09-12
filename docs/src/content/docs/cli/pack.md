---
title: macrodeck-plugin pack
description: Build a .macroDeckPlugin artifact from a payload directory, validating the manifest first.
---

Packs a payload directory into a `.macroDeckPlugin` artifact, validating the manifest and recomputing
`files[]` from disk first.

## Examples

Pack a staged payload directory:

```bash
macrodeck-plugin pack --source stage
```

```text
warning entrypoint-not-packed: Entrypoint 'linux-x64' declares 'runtimes/linux-x64/MyPlugin', which is not in the artifact.
warning entrypoint-not-packed: Entrypoint 'win-x64' declares 'runtimes/win-x64/MyPlugin.exe', which is not in the artifact.
warning publication-metadata-missing: 'repository' is required to publish to the Macro Deck plugin ecosystem. It is not required to develop or run this plugin locally.
Packed com.example.my-plugin 1.0.0 -> com.example.my-plugin-1.0.0.macroDeckPlugin (346 entries, 118565046 bytes uncompressed).
```

Write to a chosen path, overwrite it, and print the digest to sign:

```bash
macrodeck-plugin pack --source stage --output dist/my-plugin.macroDeckPlugin --force --show-digest
```

```text
...
Created output directory '~/src/MyPlugin/dist'.
Packed com.example.my-plugin 1.0.0 -> dist/my-plugin.macroDeckPlugin (346 entries, 118565046 bytes uncompressed).
Digest to sign (base64): bWFjcm8tZGVjay1wbHVnaW4vMQpjb20uZXhhbXBsZS5teS1wbHVnaW4K...
```

Packing again without `--force`:

```text
error output-exists: '~/src/MyPlugin/com.example.my-plugin-1.0.0.macroDeckPlugin' already exists. Pass --force to overwrite it.
```

A bad manifest never becomes an artifact - it is reported exactly as `validate` would:

```bash
macrodeck-plugin pack --source broken
```

```text
error invalid-version: '1.0' is not a valid SemVer version. [/version]
warning unknown-permission: 'host:everything' is not a known permission. [/permissions/0]

com.example.my-plugin 1.0: 1 error(s), 1 warning(s).
```

## Options

| Option | Default | Description |
| --- | --- | --- |
| `--source <dir>` | `.` | The payload directory to pack. |
| `--manifest <path>` | `<source>/manifest.json` | The manifest to pack. |
| `--output <path>` | `<id>-<version>.macroDeckPlugin` | Where to write the artifact. |
| `--force` | off | Overwrite an existing output file. |
| `--show-digest` | off | Also print the packed manifest's signable digest, base64-encoded, re-read from the written artifact. |

`--output` is a file path, not a format selector: `pack` has no `--output text|json`, and its report is always
plain text.

## What pack does

1. Validates the manifest with the same validator as [`validate`](/cli/validate/). Any error stops `pack`
   before a byte is written.
2. Hashes every file under `--source` except `manifest.json`, the `--output` file and any `.macroDeckPlugin` file into a fresh `files[]`. Any `files[]` the source
   manifest declared is discarded, never merged.
3. Stops on a symlink, an unsafe path, or any artifact size or entry limit from the
   [`.macroDeckPlugin` artifact section](/reference/plugin-hosting/#the-macrodeckplugin-artifact).
4. Writes the archive, creating the output directory if needed and saying so.

[`build`](/cli/build/) calls this same implementation after staging, so built and packed artifacts are the
same kind. Use `pack` when a custom build system already produced the payload.

## Warnings

None of these change the exit code:

| Code | When |
| --- | --- |
| `entrypoint-not-packed` | A declared entrypoint, per RID, is not in the payload. `build` turns this into a failure for every RID it builds. |
| `publication-metadata-missing` | A `publication` field is missing; always evaluated at the Publication level, and `pack` has no `--level`. |
| `generated-field-authored` | The source manifest in an unbuilt project tree already has `files` or `signature`. |
| `languages-recomputed` | The derived `languages` list replaced a different declared one. |
| `source-looks-like-debug-build` | `--source` looks like `bin/Debug/...`; pack a Release build for distribution. |

## Languages

When the manifest sits in a project tree, `pack` derives [`languages`](/reference/manifest/#languages) from
the project's `Localization/*.resx`, as `build` does, and the derived list wins (`languages-recomputed`). A
staged payload without the `.resx` keeps whatever the manifest declares.

## Signature

`pack` never signs. It passes any existing `signature` through, but since `files[]` was recomputed that
signature no longer matches. A packed artifact is meant to be unsigned: the Creator Portal, or
[`sign`](/cli/signing/#sign) for a non-Store artifact, signs it afterwards.

## Exit codes

| Code | Meaning |
| --- | --- |
| `0` | Packed (warnings allowed). |
| `1` | Invalid manifest, `source-entry-rejected` or `limit-exceeded`. |
| `2` | `output-exists` without `--force`. |
| `3` | `source-not-found`, or the manifest could not be read. |
| `70` | `write-failed`. |

## See also

- [`build`](/cli/build/)
- [`validate`](/cli/validate/)
- [`inspect`](/cli/inspect/)
- [`signing`](/cli/signing/)
