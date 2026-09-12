---
title: macrodeck-plugin inspect
description: Describe what installing an artifact or version directory would find, without a running host.
---

Reports what installing an artifact or version directory would find, without a running host and without
writing anything.

## Examples

Inspect a packed artifact:

```bash
macrodeck-plugin inspect --artifact com.example.my-plugin-1.0.0.macroDeckPlugin
```

```text
warning entrypoint-not-packed: Entrypoint 'linux-x64' declares 'runtimes/linux-x64/MyPlugin', which is not in the artifact.
warning entrypoint-not-packed: Entrypoint 'win-x64' declares 'runtimes/win-x64/MyPlugin.exe', which is not in the artifact.
com.example.my-plugin 1.0.0 (My Plugin)
A Macro Deck plugin.

Entrypoints:
  linux-x64: runtimes/linux-x64/MyPlugin (missing)
  osx-arm64: runtimes/osx-arm64/MyPlugin
  win-x64: runtimes/win-x64/MyPlugin.exe (missing)

Permissions: (none declared)

Languages: (none declared)

Dependencies: (none declared)

Conflicts: (none declared)

Icon packs: (none declared)

Compatibility:
  macroDeck: >=3.0.0

Signature: (not signed)

Entries: 346, uncompressed: 118565046 bytes, archive: 46689899 bytes, ratio: 2.5:1
```

Inspect a version directory before packing it:

```bash
macrodeck-plugin inspect --directory stage
```

Print the digest a signature is computed over:

```bash
macrodeck-plugin inspect --artifact com.example.my-plugin-1.0.0.macroDeckPlugin --show-digest
```

```text
...
Digest to sign (base64): bWFjcm8tZGVjay1wbHVnaW4vMQpjb20uZXhhbXBsZS5teS1wbHVnaW4K...
```

Machine-readable output:

```bash
macrodeck-plugin inspect --artifact com.example.my-plugin-1.0.0.macroDeckPlugin --output json
```

```json
{
  "pluginId": "com.example.my-plugin",
  "name": "My Plugin",
  "version": "1.0.0",
  "description": "A Macro Deck plugin.",
  "entrypoints": [
    {
      "rid": "linux-x64",
      "executable": "runtimes/linux-x64/MyPlugin",
      "arguments": [],
      "runtimeKind": "SelfContained",
      "dotnetVersion": null,
      "present": false
    },
    ...
  ],
  "permissions": [],
  "languages": [],
  "dependencies": [],
  "conflicts": [],
  "iconPacks": [],
  "compatibility": { "sdk": null, "macroDeck": ">=3.0.0", "protocolMinimum": null, "protocolMaximum": null },
  "signature": null,
  "entryCount": 346,
  "totalUncompressedBytes": 118565046,
  "archiveBytes": 46689899,
  "compressionRatio": 2.539415345490467,
  "warnings": [
    { "code": "entrypoint-not-packed", "message": "Entrypoint 'linux-x64' declares ..." },
    ...
  ],
  "digestBase64": null
}
```

Forgetting the input is a usage error - unlike `validate` and `pack`, `inspect` has no default:

```bash
macrodeck-plugin inspect
```

```text
error no-selector: Specify one of --artifact or --directory. Unlike validate and pack, inspect has no default input.
```

## Options

| Option | Default | Description |
| --- | --- | --- |
| `--artifact <path>` | - | Path to a `.macroDeckPlugin` artifact. |
| `--directory <path>` | - | A version directory containing `manifest.json`. |
| `--show-digest` | off | Also print the manifest's signable digest, base64-encoded. |
| `--output <text\|json>` | `text` | How to render the result. |

Exactly one of `--artifact` and `--directory` is required: neither is `no-selector`, both is
`too-many-selectors`.

## What is reported

Entrypoints, permissions, declared languages, dependencies, conflicts, icon packs, compatibility, signature
shape, entry count, size and compression ratio.

`inspect` describes, it does not judge: it never runs the JSON Schema, never checks a declared file's digest,
never flags an undeclared file, and marks an unknown permission `(unknown)` without failing. Use
[`validate`](/cli/validate/) for a verdict.

The one payload check is entrypoint presence: every declared entrypoint, for every RID, is checked against
the real content. A missing one prints `warning entrypoint-not-packed`, is marked `(missing)` in the text
report, and has `"present": false` in JSON (`null` when the payload could not be read, so presence was not
checked). JSON also carries a top-level `warnings[]` of `{ code, message }`. A single-platform build is a
legitimate intermediate state, so this never changes the exit code.

## Signature shape

Reported as one of `not signed`, `well-formed ed25519 (not cryptographically verified)`,
`unverifiable (unrecognized algorithm)` or `invalid ed25519 length` - shape only, as the
[signing section](/reference/plugin-hosting/#signing) describes. `inspect` has no key material to verify
against.

## Exit codes

| Code | Meaning |
| --- | --- |
| `0` | The manifest was read; there is no "read fine but invalid" outcome. |
| `2` | Usage error: `no-selector` or `too-many-selectors`. |
| `3` | The input could not be read, e.g. `artifact-not-found`, `manifest-not-found`. |

An artifact or manifest the reader rejects exits with the code for that reader error.

## See also

- [`validate`](/cli/validate/)
- [`pack`](/cli/pack/)
- [`signing`](/cli/signing/)
- [Plugin hosting](/reference/plugin-hosting/)
