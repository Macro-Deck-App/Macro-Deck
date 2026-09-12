---
title: macrodeck-plugin inspect
description: Report what installing an artifact or version directory would find, without a running host.
---

Reports what installing an artifact or version directory would find, without a running host and without
writing anything - entrypoints, permissions, declared languages, dependencies, conflicts, icon packs,
compatibility, signature shape, size and compression ratio.

| Option | Default | What it does |
| --- | --- | --- |
| `--artifact <path>` | - | Path to a `.macroDeckPlugin` artifact. |
| `--directory <path>` | - | A version directory containing `manifest.json`. |
| `--show-digest` | off | Also print the manifest's signable digest, base64-encoded - the exact bytes a signature is computed over. |
| `--output <text\|json>` | `text` | How to render the result. |

**Exactly one** of `--artifact`/`--directory` is required - unlike `validate`, giving neither or giving
both is a usage error, each reported with its own message (`no-selector` / `too-many-selectors`).

`inspect` is read-only reporting, not a second validation pass: it never runs the JSON Schema, never checks
a declared file's digest against disk, and never flags an undeclared file. An unknown permission is
annotated `(unknown)` in the report but never affects the result. Consequently, if the manifest reads at
all, `inspect` still succeeds (exit `Success`, 0) - there is no "read fine, but invalid" outcome the way
`validate` has one.

The one thing `inspect` does check against the real payload is whether every entrypoint the manifest
declares is actually present - `PluginManifestReader` only ever existence-checks the entrypoint for the
*current* runtime identifier, so a foreign RID's binary was never verified anywhere before. A missing one
is reported as `warning entrypoint-not-packed: …` and marked `(missing)` in the text report's entrypoint
list; `--output json` adds a `present` field per entrypoint - `true`/`false`, or `null` when the payload
could not be read at all and presence was therefore never checked - and a top-level `warnings[]`
array of `{ code, message }`. This still never affects the exit code or turns `inspect` into a validation
pass - a single-platform build reported this way is a legitimate intermediate state, not a defect. Use
`validate` when you need a verdict; use `inspect` when you need a description.

Signature shape is reported as one of `not signed`, `well-formed ed25519 (not cryptographically verified)`,
`unverifiable (unrecognized algorithm)`, or `invalid ed25519 length` - the same shape-only classification
the plugin hosting guide's [signing section](/reference/plugin-hosting/#signing) documents; `inspect` never has key material to check the
signature against.
