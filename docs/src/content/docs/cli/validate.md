---
title: macrodeck-plugin validate
description: Check a manifest, version directory or packed artifact and report every problem in one run.
---

Checks a manifest, a version directory or a `.macroDeckPlugin` artifact and reports every problem it finds.

## Examples

Validate a staged payload directory while developing:

```bash
macrodeck-plugin validate --directory stage
```

```text

com.example.my-plugin 1.0.0: 0 error(s), 0 warning(s).
```

Check that a plugin is ready to publish:

```bash
macrodeck-plugin validate --directory stage --level publication
```

```text
error publication-metadata-missing: 'repository' is required to publish to the Macro Deck plugin ecosystem. It is not required to develop or run this plugin locally. [/repository] (publication)
error entrypoint-not-packed: Entrypoint 'linux-x64' declares 'runtimes/linux-x64/MyPlugin', which is not in the artifact. [/entrypoints/linux-x64/executable] (package)
error entrypoint-not-packed: Entrypoint 'win-x64' declares 'runtimes/win-x64/MyPlugin.exe', which is not in the artifact. [/entrypoints/win-x64/executable] (package)

com.example.my-plugin 1.0.0: 3 error(s), 0 warning(s).
```

Validate a packed artifact (defaults to `--level package`):

```bash
macrodeck-plugin validate --artifact com.example.my-plugin-1.0.0.macroDeckPlugin
```

```text
warning publication-metadata-missing: 'repository' is required to publish to the Macro Deck plugin ecosystem. It is not required to develop or run this plugin locally. [/repository] (publication)
error entrypoint-not-packed: Entrypoint 'linux-x64' declares 'runtimes/linux-x64/MyPlugin', which is not in the artifact. [/entrypoints/linux-x64/executable] (package)
error entrypoint-not-packed: Entrypoint 'win-x64' declares 'runtimes/win-x64/MyPlugin.exe', which is not in the artifact. [/entrypoints/win-x64/executable] (package)

com.example.my-plugin 1.0.0: 2 error(s), 1 warning(s).
```

A manifest with several defects - every independent problem is reported at once:

```bash
macrodeck-plugin validate --directory broken
```

```text
error invalid-version: '1.0' is not a valid SemVer version. [/version]
warning unknown-permission: 'host:everything' is not a known permission. [/permissions/0]

com.example.my-plugin 1.0: 1 error(s), 1 warning(s).
```

Pointing at the project instead of the build output:

```bash
cd ~/src/MyPlugin/src/MyPlugin
macrodeck-plugin validate
```

```text
error source-directory: Entrypoint 'osx-arm64' resolves to '~/src/MyPlugin/src/MyPlugin/runtimes/osx-arm64/MyPlugin', which does not exist. This looks like a source directory - validate the build output instead, e.g. bin/Release/net10.0.

~/src/MyPlugin/src/MyPlugin/manifest.json: 1 error(s), 0 warning(s).
```

Machine-readable output for CI:

```bash
macrodeck-plugin validate --directory stage --level publication --output json
```

```json
{
  "valid": false,
  "pluginId": "com.example.my-plugin",
  "version": "1.0.0",
  "level": "publication",
  "problems": [
    {
      "severity": "error",
      "code": "publication-metadata-missing",
      "message": "'repository' is required to publish to the Macro Deck plugin ecosystem. ...",
      "pointer": "/repository",
      "requiredBy": "publication"
    },
    ...
  ]
}
```

## Options

| Option | Default | Description |
| --- | --- | --- |
| `--manifest <path>` | `./manifest.json` | Path to a `manifest.json` file. |
| `--directory <path>` | - | A version directory containing `manifest.json`. |
| `--artifact <path>` | - | Path to a `.macroDeckPlugin` artifact. |
| `--level <development\|package\|publication>` | from the selector | How strictly to validate - see [Levels](#levels). |
| `--output <text\|json>` | `text` | How to render the result. |

Give at most one of `--manifest`, `--directory` and `--artifact` (more is `too-many-selectors`, exit 2). Giving
none validates `./manifest.json`, so running `validate` inside a plugin's build output needs no flag.

## Levels

The three cumulative [requirement levels](/reference/manifest/#requirement-categories),
`development ⊂ package ⊂ publication`:

| Level | Adds over the level before |
| --- | --- |
| `development` | The manifest reader, the embedded JSON Schema, the permission vocabulary, SemVer `version`, and declared `files[]` digests when present. What the host enforces at install time. |
| `package` | Every declared entrypoint (every RID, not only the current host's) and a declared `icon` checked against real content; a valid multi-RID layout; missing `publication` fields as warnings. |
| `publication` | Missing `publication` fields become errors. |

When `--level` is omitted, `--manifest`/`--directory` (or no selector) use `development` and `--artifact` uses
`package`. An unrecognised level is a usage error (exit 2) reported before the manifest is read:

```text
error usage-error: 'strict' is not a recognized --level. Expected one of: development, package, publication.
```

On an unbuilt source tree (a `manifest.json` next to a project file) the `package`/`publication` payload and
layout checks are skipped, so validating a project root before building does not report files that do not
exist yet.

## What is checked

- The schema and permission-vocabulary checks run for every input. An unknown permission is a warning.
- `version` is checked against SemVer independently of the reader: `1.0` or `v1.0.0` is `invalid-version`.
- File digests are checked only when the manifest declares `files[]`. Only `--artifact` also flags a file
  present but not declared, because a bare manifest or directory has no separate file listing.
- A schema error that is only a follow-on of another reported problem beneath it is suppressed, so one defect
  never appears twice.

## Problem codes

| Code | Meaning |
| --- | --- |
| `malformed` | Not valid JSON; the message names the file with a 1-based line and position. |
| `invalid-version` | `version` is not SemVer. |
| `unknown-permission` | A permission outside the vocabulary (warning). |
| `schema:<keyword>` | A JSON Schema violation, e.g. `schema:required`. |
| `file-missing`, `file-size-mismatch`, `file-digest-mismatch` | A declared `files[]` entry does not match the real bytes. |
| `undeclared-file` | A file in the artifact that `files[]` does not declare (`--artifact` only). |
| `entrypoint-not-packed` | A declared entrypoint, for any RID, is not in the content (`package` and above). |
| `icon-declared-not-present` | `icon` is declared but not in the content (error, `package` and above). |
| `entrypoint-layout-invalid` | Two entrypoints stage into the same directory, or one stages at the package root (error, `package` and above) - the same rule [`build`](/cli/build/) enforces, see [Staging layout](/cli/build/#staging-layout). |
| `publication-metadata-missing` | A `publication`-required field is missing or blank (warning at `package`, error at `publication`). |
| `generated-field-authored` | `files` or `signature` in a manifest that has not been built or packed yet (warning, unbuilt source tree only). |
| `source-directory` | The manifest sits next to a project file with no built entrypoint - validate the build output instead. |
| `not-an-artifact` | `--artifact` is not a ZIP; adds `Did you mean validate --manifest?` when the file is named `manifest.json`. |
| `manifest-not-found`, `artifact-not-found` | The input does not exist. |

Other manifest reader failures are reported under their own kebab-case code, e.g. `entrypoint-missing`.

## Output

Text output is one line per problem - severity, code, message, the JSON pointer in `[...]` when there is one,
and the requiring level in `(...)` when a level requires it - then a summary line. The summary names
`<id> <version>`, or the resolved absolute path when the manifest could not be read.

`--output json` returns `valid`, `pluginId`, `version`, `level` (always present: the level actually used) and
`problems[]`. Each problem has `severity`, `code`, `message` and `pointer`, plus `requiredBy` only when a level
requires it (for example absent on `generated-field-authored`).

## Exit codes

| Code | Meaning |
| --- | --- |
| `0` | Valid at the chosen level (warnings allowed). |
| `1` | The manifest was read but has at least one error. |
| `2` | Usage error: too many selectors, unknown `--level`. |
| `3` | The input could not be read: missing file, not a ZIP, permissions. |

## See also

- [`inspect`](/cli/inspect/) - describe an artifact without judging it
- [`pack`](/cli/pack/) - runs the same validation before writing an artifact
- [`build`](/cli/build/)
- [Manifest reference](/reference/manifest/)
