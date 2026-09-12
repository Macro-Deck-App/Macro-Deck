---
title: macrodeck-plugin pack
description: Build a .macroDeckPlugin artifact from a payload directory, validating the manifest and recomputing file digests first.
---

Builds a `.macroDeckPlugin` artifact from a source tree: validates the manifest first with the exact same
reader `validate` uses, then recomputes `files[]` from what is actually on disk.

| Option | Default | What it does |
| --- | --- | --- |
| `--source <dir>` | `.` | The payload directory to pack. |
| `--manifest <path>` | `<source>/manifest.json` | Path to the manifest to pack. |
| `--output <path>` | `<id>-<version>.macroDeckPlugin` | Where to write the artifact. A file path, not a format selector - `pack` has no `--output text\|json`, since a file path and a format flag under the same name would collide. |
| `--force` | off | Overwrite an existing output file. |
| `--show-digest` | off | Also print the packed manifest's signable digest, base64-encoded - the exact bytes a signature is computed over, re-derived from the artifact `pack` just wrote. |

A bad manifest never becomes an artifact: the same `ManifestValidator` `validate` runs is the first step,
and any problem it finds stops `pack` before a single byte is written, reported the same way `validate`
would report it. Every file under `--source` except `manifest.json` itself, the `--output` file and any
`.macroDeckPlugin` file is then hashed from disk and becomes a fresh `files[]` entry - **whatever `files[]`
the source manifest already declared is discarded, never merged or compared against**. A symlink, an unsafe
path, or any of the artifact size/entry limits the plugin hosting guide's [`.macroDeckPlugin` artifact section](/sdk/hosting/#the-macrodeckplugin-artifact) documents also stops the pack before
writing.

[`build`](/cli/build/) calls this exact implementation once it has staged a payload, so a built package and a
packed one are the same kind of artifact; `pack` remains the right command whenever you already have a
payload directory, from a custom build system or any other workflow.

`pack` checks the same entrypoint-presence rule `inspect` does: every entrypoint the manifest declares is
checked against what is actually being packed, and a missing one prints `warning entrypoint-not-packed: …`
per runtime identifier - exit code stays `Success` (0), since packing only the platform you happen to be
building on is a legitimate intermediate state, not a defect worth failing the build over. `build` holds a
stronger contract and turns that warning into a failure for every runtime identifier it was asked to build.
`pack` also
warns (`warning source-looks-like-debug-build`) when `--source` looks like a Debug build
(`bin/Debug/...`) - pack a Release build for distribution instead - and reports when it had to create a
missing output directory, rather than doing so silently.

Like [`build`](/cli/build/), `pack` evaluates the manifest at the Publication requirement level and reports
every unsatisfied `publication` field as a `publication-metadata-missing` warning - exit code stays
`Success` (0); a publication-incomplete but structurally sound plugin still packs. `pack` takes no
`--level` flag; this check always runs.

**`languages` is derived where it can be.** When the manifest sits in a project tree, `pack` derives
[`languages`](/reference/manifest/#languages) from that project's `Localization/*.resx` set, exactly as
[`build`](/cli/build/) does. When it does not - a staged or published payload directory, which no longer
carries the `.resx` those tags come from - whatever the manifest declares is carried through untouched,
since that value is then the only remaining record of what went into the build. Where `pack` can derive a
list *and* the manifest declares a different one, the derived list wins and the replacement is reported as
a `languages-recomputed` warning rather than done silently; the exit code stays `Success` (0).

**`signature` passes through untouched.** `pack` itself never signs anything - it preserves whatever
`signature` the source manifest already had rather than dropping it, but since `files[]` was just
recomputed, a manifest that was signed before packing carries a signature that no longer matches its own
digest. A packed artifact is meant to be unsigned: that is what the Creator Portal expects to receive,
and it is what anything that signs one - the Portal, or [`sign`](/cli/signing/#sign) for a non-Store artifact - signs
afterwards, against the packed artifact's own manifest.

`pack` has no output-format flag; its report is always plain text, and a validation failure is rendered the
same way `validate --output text` would render it regardless of anything else on the command line.
