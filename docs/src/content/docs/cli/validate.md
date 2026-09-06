---
title: macrodeck-plugin validate
description: Validate a manifest, a version directory or a packed artifact against the real manifest reader, the schema, the permission vocabulary and declared file digests.
---

Validates a manifest, a version directory, or a packed artifact: the real manifest reader, the embedded
JSON Schema, the permission vocabulary, and - when the manifest declares `files[]` - declared file digests
checked against real bytes.

| Option | Default | What it does |
| --- | --- | --- |
| `--manifest <path>` | - | Path to a `manifest.json` file. |
| `--artifact <path>` | - | Path to a `.macroDeckPlugin` artifact. |
| `--directory <path>` | - | A version directory containing `manifest.json`. |
| `--output <text\|json>` | `text` | How to render the result. |
| `--level <development\|package\|publication>` | implied by the selector | How strictly to validate - see below. |

At most one of `--manifest`/`--artifact`/`--directory` may be given. **Giving none is legal and validates
`./manifest.json`** in the current directory - the default a plugin's own build output directory
satisfies without any flag at all.

## `--level`

Validates against one of the three cumulative [requirement levels](/reference/manifest/#requirement-categories)
- `Development ⊂ Package ⊂ Publication` - each a strict superset of the checks the level before it runs:

| Level | Adds over the level before | Can exit |
| --- | --- | --- |
| `development` | Everything above, unconditionally: the manifest reader, the schema, the permission vocabulary, and declared `files[]` digests when present. This is the floor - what the host itself enforces at install time. | `Success` (0) or `SubjectInvalid` (1). Never fails on missing `publication` metadata; that check does not run at this level. |
| `package` | Every declared entrypoint checked against real packaged content (not only the current host's RID), a declared `icon` checked the same way, a valid multi-RID entrypoint layout, and every unsatisfied `publication` field reported as a **warning**. | `Success` (0) or `SubjectInvalid` (1) - a payload or layout problem fails it; missing publication metadata alone does not. |
| `publication` | Package, with unsatisfied `publication` fields promoted from warning to **error**. | `Success` (0) or `SubjectInvalid` (1) - now including missing publication metadata alone. |

`--level` is implied by the selector when omitted: `--manifest`/`--directory` (still being developed)
default to `development`; `--artifact` (already packed) defaults to `package`. An unrecognized `--level`
token is a `UsageError` (2) reported before the manifest is read at all.

The `package` and `publication` payload/layout checks (entrypoint and icon presence, layout) are
suppressed when the subject looks like an unbuilt source tree (a `manifest.json` next to a project file)
- validating a project root before building does not complain about files that do not exist yet.

## Requirement-related problem codes

| Code | Severity | Level | Meaning |
| --- | --- | --- | --- |
| `publication-metadata-missing` | Warning at `package`, error at `publication` | Package and above | A `publication`-required field is missing or blank. |
| `generated-field-authored` | Warning | Any level, only on an unbuilt source tree | `files` or `signature` is present in a manifest that has not been built or packed yet. An extracted install directory or a packed artifact legitimately carries these, so the warning is suppressed there. |
| `icon-declared-not-present` | Error | Package and above | The manifest declares `icon`, but it is not present in the packaged content. |
| `entrypoint-layout-invalid` | Error | Package and above | Two declared entrypoints would stage into the same directory, or one stages at the package root - the same rule [`build`](/cli/build/) hard-fails on (see [Staging layout](/cli/build/#staging-layout)), so `build` and `validate` can never disagree about the same manifest. |

`entrypoint-not-packed` (pre-existing) now checks **every declared runtime identifier**, not only the
current host's - the manifest reader itself only ever existence-checks the current RID's own entrypoint,
so a foreign RID's binary was previously unverified anywhere in `validate`.

The schema and permission-vocabulary checks run unconditionally for every input shape. The file-digest
check only runs when the manifest declares `files[]`, and only an artifact input also checks for a file
present on disk but **not** declared - a bare manifest or directory has no independent file listing to
compare against. `version` is checked against SemVer independently of the manifest reader's own checks -
`'1.0'` or `'v1.0.0'` is now an `invalid-version` error even for a manifest that otherwise reads fine,
which a looser version string could pass before.

`validate` reports every independent problem it finds in one run, not just the first - a manifest with
several unrelated defects no longer needs several rounds of fixing one, rerunning, and finding the next.
A pure follow-on (a generic "something under here failed" schema hit whose own cause is reported
separately, at a location beneath it) is suppressed so one defect never shows up as two.

A few common mistakes now get a message that names the actual problem instead of leaking an internal
exception or reading as "this plugin is broken": a `--artifact` that is not a ZIP at all now says
`'<path>' is not a .macroDeckPlugin artifact (not a ZIP archive)`, plus `Did you mean validate
--manifest?` when the file is named `manifest.json`; a manifest that is not valid JSON now names the file
and gives a real 1-based line and position; and running `validate`/`pack` against a source tree - a
`manifest.json` next to a `.csproj` with no built entrypoint - is now diagnosed as pointing at the wrong
directory (build the project and validate the output instead) rather than reading as a broken manifest.
Exit codes for all three are unchanged.

Exit code is `SubjectInvalid` (1) whenever the reader succeeds but finds a problem at any layer,
`InputUnreadable` (3) when the input itself cannot be read, and `Success` (0) otherwise - see the
`--level` table above for exactly which problems can produce `SubjectInvalid` at each level.

Text output is one line per problem: `error`/`warning`, the problem's code, its message, a schema
pointer when there is one, and - new - the requiring level in parentheses when the problem is one a
specific level requires, e.g. `error publication-metadata-missing: ... [/license] (publication)`,
followed by a one-line summary.

`--output json` reports `valid`, `pluginId`, `version`, a **new top-level `level`** (always present -
the level the run actually used), and `problems[]`. Each problem keeps its four existing keys
(`severity`, `code`, `message`, `pointer`) and gains a **new `requiredBy`** key, present only for a
problem a level actually requires (e.g. `"publication"` for `publication-metadata-missing`, omitted for
a `generated-field-authored` warning, since no level requires a generated field to be *absent*). The
four pre-existing keys and their meaning are unchanged; `level` and `requiredBy` are additive.

The summary line names the resolved, absolute path of whatever was validated when the manifest could not
even be read - an unreadable id is not something to echo back as the heading.
