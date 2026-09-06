---
title: Manifest reference
description: Every field of a Macro Deck plugin manifest.json - types, required-ness, patterns, defaults and clamped ranges - with a complete worked example.
---

`manifest.json` is what a plugin declares itself to be. It sits at the root of a `.macroDeckPlugin`
artifact, at the root of an installed version directory, and - during development - at the plugin's
content root, where the SDK reads it at startup.

The machine-checkable shape is published as
[`plugin-manifest-v1.schema.json`](/schemas/plugin-manifest-v1.schema.json)
(`$id`: `https://schemas.macro-deck.app/plugin-manifest-v1.schema.json`). Name it in `$schema` and your
editor validates and autocompletes the file for free.

Property names are camelCase, because the host serialises and reads this document with a camelCase
naming policy. **Unknown properties are ignored, everywhere, deliberately** - an older host reading a
manifest that declares a field it does not know about behaves exactly as it did before that field
existed. Do not rely on a schema that rejects unknown properties; the host itself accepts them.

## Requirement categories

Every field in the schema is annotated with which of four categories it belongs to. The category
decides *when* a field is required, not whether it is well-formed if present - a `runtime` field is
what actually gates an install; the rest is metadata quality, requested by different consumers at
different points in a plugin's life:

| Category | Meaning |
| --- | --- |
| `runtime` | Required for the plugin to install and run at all. The host's manifest reader already rejects a manifest missing one of these. |
| `publication` | Not required to develop or run the plugin locally. Required before the plugin is accepted into the public Macro Deck plugin ecosystem. |
| `recommended` | Never required at any level. Filling it in only improves what the plugin can do or how it is presented. |
| `generated` | Produced by the packaging pipeline (`macrodeck-plugin build`/`pack`), never hand-authored. `files` and `signature` are the two fields in this category - see their own sections below. |

Those categories feed three cumulative validation levels - `Development ⊂ Package ⊂ Publication` - each
a strict superset of the checks before it, so a manifest that is valid at a lower level never becomes
invalid at that same level later:

| Level | What it adds | Who runs it |
| --- | --- | --- |
| Development | Every `runtime` field, plus manifest/entrypoint/compatibility shape. This is the floor: it never becomes stricter, so a manifest that installs locally today keeps installing. | The Macro Deck host, at install time. |
| Package | Development, plus checks that need real packaged content (declared entrypoints and icon present in the payload, a valid multi-RID layout), plus every unsatisfied `publication` field as a **warning**. | `macrodeck-plugin build`, `pack` and `validate --level package`. |
| Publication | Package, with unsatisfied `publication` fields promoted from warning to **error**. | `macrodeck-plugin validate --level publication`, and the Creator Portal at upload. |

See [`validate --level`](/cli/validate/) for the CLI side of this, and
[Publishing to the Store](/guides/publishing/) for what the Portal checks. The host itself only ever
enforces Development - see [Plugin hosting](/sdk/hosting/).

### The `x-macrodeck-requirement` annotation

Each property in [the published schema](/schemas/plugin-manifest-v1.schema.json) carries a non-standard
`x-macrodeck-requirement` annotation valued `"runtime"`, `"publication"`, `"recommended"` or
`"generated"` - exactly the four categories above. It is an annotation keyword, so every JSON Schema
validator ignores it; no document that validated against this schema before stops validating, and the
schema's `$id` and top-level `required` array are unchanged. Tooling outside this repository can read
the category for any field straight from the published schema rather than hardcoding this table.
`MacroDeck.Plugin.Packaging.Manifest.PluginManifestRequirements` reads the same annotations from the
schema (embedded as a resource in that package) as the SDK's own single source of truth, rather than
restating the categories in C#.

## Runtime-required fields

These five fields are `runtime`: the host's manifest reader already rejects a manifest missing one of
them, so they are also the schema's top-level `required` array.

| Field | Type | Requirement | Rules |
| --- | --- | --- | --- |
| `manifestVersion` | integer | Runtime | Must be exactly `1`. Anything else is rejected before the rest of the document is parsed, so an unsupported future manifest reports precisely that rather than a confusing schema error. |
| `id` | string | Runtime | A reverse-domain id (see [package-ID rules](#package-id-rules)). Must match the id directory name the manifest is installed under. |
| `name` | string | Runtime | 1–128 characters. Must not contain control characters - a rule the host enforces and the schema cannot express. |
| `version` | string | Runtime | SemVer 2.0. Must match the version directory name the manifest is installed under. |
| `entrypoints` | object | Runtime | At least one entry. Keyed by .NET runtime identifier - see [entrypoints](#entrypoints). Each entry's own `executable` is Runtime too. |

## Package-ID rules

`id` is a reverse-domain identifier, matching:

```
^[a-z][a-z0-9]*(-[a-z0-9]+)*(\.[a-z][a-z0-9]*(-[a-z0-9]+)*)+$
```

In words:

- Lowercase only. No uppercase, no underscores.
- Segments joined by dots, **at least two** of them - `myplugin` is invalid, `com.example.my-plugin`
  is valid.
- Each segment starts with a letter, then letters and digits, optionally with hyphen-separated
  sub-segments (`my-plugin`). No leading, trailing or doubled hyphens.
- At most 128 characters.

The same pattern validates `publisher.id`, every `dependencies[].id`, every `conflicts[].id` and every
`iconPacks[].id`.

`id` and `version` are not free-standing labels: an installed plugin lives at
`<dataRoot>/plugins/<id>/versions/<version>/`, and a manifest whose `id` or `version` disagrees with
the directory it was installed into is rejected. The leading-underscore directories `_staging` and
`_cache` are never mistaken for a plugin id precisely because an underscore is not legal in this
pattern.

## Descriptive fields

| Field | Type | Requirement | Notes |
| --- | --- | --- | --- |
| `description` | string | Publication | Human-readable. Not required to develop or run the plugin locally; required before it is accepted into the public ecosystem. |
| `icon` | string | Publication | Forward-slash separated path, relative to the version directory: no `..` segment, no absolute path. Lets a listing show an icon for a plugin that is not running; a running plugin's icon still travels over the asset pipeline. **The file is deliberately not checked to exist** at Development level - the same reader gates every launch, so an existence check would let a missing icon stop a working plugin. At Package level and above, `macrodeck-plugin validate`/`build`/`pack` do check that a declared icon is actually present in the packaged content (`icon-declared-not-present`). When `files[]` is present, list the icon there like any other payload file. |
| `license` | string | Publication | SPDX identifier by convention. Carried verbatim, never parsed. |
| `homepage` | string | Recommended | Must be an absolute `http` or `https` URL; anything else is rejected. Informational. Never required, at any level. |
| `repository` | string | Publication | Same URL rule as `homepage`. Informational. |

### `publisher`

An object; `name` is required if the block is present, and the block itself is Publication-required.

| Field | Type | Requirement | Notes |
| --- | --- | --- | --- |
| `name` | string | Publication | Required within the block, non-empty. Display name. |
| `id` | string | Recommended | Reverse-domain id, validated like a plugin id, when present. |
| `email` | string | Recommended | Contact email. |
| `url` | string | Recommended | Absolute `http`/`https` URL. |

`publisher` identifies the Creator or Organization account that owns the listing - it is not free text.
Locally it is still just a claim the manifest makes about itself: local development contacts no server
and needs no Platform access, so nothing checks it before a plugin runs. At upload, the Creator Portal is
responsible for checking that `publisher.name` matches the authenticated Creator/Organization account and
that `publisher.id`, **when present**, is that account's id - which is why `publisher.id` stays
Recommended rather than Publication-required: the ownership check does not depend on it being filled in.
See [signature](#signature), [Publishing to the Store](/guides/publishing/) and
[the security model](/policies/security/) for how signing, as opposed to this field, actually
authenticates an artifact.

## Entrypoints

`entrypoints` maps a .NET runtime identifier to a launch target. At least one entry is required.

```json
"entrypoints": {
  "win-x64": { "executable": "MyPlugin.exe", "arguments": ["--quiet"] },
  "osx-arm64": { "executable": "MyPlugin" },
  "linux-x64": {
    "executable": "MyPlugin.dll",
    "runtime": { "kind": "FrameworkDependent", "dotnetVersion": "10.0" }
  }
}
```

| Field | Type | Requirement | Notes |
| --- | --- | --- | --- |
| `executable` | string | Runtime | **Required.** Path relative to the version directory, which it must stay inside: no `..`, no absolute path. Never a script - `.sh`, `.bat`, `.cmd`, `.ps1` and `.command` are rejected outright regardless of `runtime`. |
| `arguments` | string[] | Recommended | Passed to the executable in order. |
| `runtime` | object | Recommended | How the entrypoint expects to be launched. Omitted means self-contained. |

`runtime`:

| Field | Type | Default | Requirement | Notes |
| --- | --- | --- | --- | --- |
| `kind` | string | `SelfContained` | Recommended | `SelfContained` or `FrameworkDependent`. Deserialised case-insensitively. |
| `dotnetVersion` | string | — | Recommended | `major.minor`, e.g. `"10.0"`. Required when `kind` is `FrameworkDependent`, ignored otherwise. |

Two cross-field rules the schema cannot express, enforced by the reader: `FrameworkDependent` requires
`executable` to end in `.dll`; absent or `SelfContained` requires it **not** to.

For the multi-platform, per-RID directory layout `macrodeck-plugin build`/`pack` expect an `executable`
path to follow (so two platforms' identically-named binaries never collide), see the CLI guide's
[Staging layout](/cli/build/#staging-layout) - that is the canonical description; this page does
not repeat it. `macrodeck-plugin build` and `validate --level package` share one rule for judging that
layout, so the two can never disagree about the same manifest (`entrypoint-layout-invalid`).

### Runtime identifier resolution

Keys are resolved against the host's own RID:

- An exact match is used if present.
- Failing that, exactly two silicon fallbacks apply: `osx-arm64 → osx-x64` and `win-arm64 → win-x64`.
- There is no `"any"` key and no other fallback. `linux-musl-*` resolves nothing, deliberately: a
  manifest shipping only `linux-x64` simply has no entrypoint for a musl host.

An artifact with no entrypoint for this host still installs and activates normally; the installer adds
an advisory warning and does not start the plugin. Only the entrypoint(s) matching the current RID are
checked for existence on disk.

## Timing settings

Every numeric setting below is **clamped, never rejected** - an out-of-range value is a foot-gun, not
grounds to refuse the plugin.

### `shutdown`

| Field | Type | Default | Clamped to | What it does |
| --- | --- | --- | --- | --- |
| `gracefulTimeoutSeconds` | integer | 10 | 1–60 | Seconds given to the plugin to exit after `session.goodbye` and the WebSocket close, before a process-tree kill. |

### `health`

| Field | Type | Default | Clamped to | What it does |
| --- | --- | --- | --- | --- |
| `path` | string | `/_macrodeck/health` | — | The SDK health route the probe calls. |
| `intervalSeconds` | integer | 15 | 5–120 | Seconds between probes. |
| `timeoutSeconds` | integer | 2 | 1–10 | Seconds before one probe is considered failed. |
| `unhealthyThreshold` | integer | 3 | 2–10 | Consecutive failures before the plugin is considered unhealthy. |

The floor of 2 on `unhealthyThreshold` is deliberate: "a single missed health check never restarts a
plugin" is a structural property of the manifest format, not something a plugin author can configure
away.

## `compatibility`

The `compatibility` block itself is Publication-required: declare at least one of `sdk`, `protocol` or
`macroDeck` before publishing. Every member is optional within the block, and an absent member declares
nothing - it never means "incompatible".

| Field | Type | Requirement | Enforced |
| --- | --- | --- | --- |
| `sdk` | version range | Recommended | Recorded but **not** checked at install time - the host cannot know which SDK a plugin used until it connects. |
| `protocol` | `{ minimum, maximum }` | Recommended | Checked when present. Outside the host's range **rejects** the install. `minimum` and `maximum` are individually Runtime - once `protocol` is declared at all, the reader requires both. |
| `macroDeck` | version range | Recommended | Checked when present. Outside the range **rejects** the install. Skipped entirely on a development host build (`0.0.0-dev`). |

Failing a compatibility check is a rejection, not a warning: an artifact declaring it needs a host this
one is not is malformed for this host.

`protocol` is an inclusive integer range of protocol majors, reused verbatim from the protocol package
so the manifest and the wire handshake cannot drift. `minimum` is at least 1, and `maximum` must be at
least `minimum`.

### Version range grammar

`compatibility.sdk`, `compatibility.macroDeck` and every `versionRange` below share one deliberately
small, closed grammar:

```
*                      any version
>=1.2.0                one comparator
>=1.2.0,<2.0.0         comma means AND
```

Comparators are `=`, `>`, `>=`, `<`, `<=`. There is **no caret, tilde, `||` or wildcard** - each means
something different across npm, NuGet and Cargo. Precedence is plain SemVer 2.0, so `>=1.0.0` is not
satisfied by `1.0.0-beta.1`. Whitespace around comparators and commas is trimmed, so
`">= 1.0.0, < 2.0.0"` is accepted.

## `permissions`

Requirement: Recommended - never required at any level. A flat array of unique strings. The known
vocabulary mirrors the host callback surface one for one:

`host:variables`, `host:user-variables`, `host:config`, `host:deck`, `host:scripts`, `host:widgets`,
`host:notifications`, `host:action-interactions`, `host:devices`, `host:variable-values`,
`host:layouts`, `host:folder-views`, `host:widget-types`, `events:publish`, `assets:upload`,
`net:outbound`,
`fs:user-files`, `process:spawn`, `device:usb`.

Deliberately not an enum: an **unknown** permission is advisory, not invalid, so a manifest naming a
permission this host does not yet know about still installs. A **duplicated** permission string is
rejected.

**Declared, validated, persisted and exposed - enforced nowhere.** Nothing in the host gates a callback
on whether the calling plugin declared the permission that covers it. See
[the security model](/policies/security/#permissions-declared-not-enforced).

## `languages`

Requirement: Recommended - never required at any level. A flat array of unique
[BCP-47](https://www.rfc-editor.org/info/bcp47) tags naming the languages the plugin's own user-facing
strings are available in:

```json
"languages": ["en", "de", "zh-Hant-TW"]
```

**You do not normally write this by hand.** `macrodeck-plugin build` and `macrodeck-plugin pack` derive
it from the plugin project's `Localization/*.resx` set - the unsuffixed `Strings.resx` counts as `en`,
and every culture-suffixed sibling contributes its own tag - so the manifest cannot claim a language the
plugin does not actually ship. See [packing and the localization guide](/sdk/localization/#the-manifest-languages-field).
A value you did write survives when the tooling has nothing to derive from (packing a payload directory
with no resource files in it); where it *can* derive one and the two disagree, the derived list wins and
`pack` says so.

**Full tags, never truncated to two letters.** `zh-Hans` and `zh-Hant` are different languages to a
reader and both would collapse onto `zh`, as would `pt-BR` and `pt-PT`. A consumer that wants a
two-letter grouping can derive it from the full tag; nothing can go the other way. The shape accepted is
a 2–3 letter language, an optional 4-letter script, and an optional 2-letter or 3-digit region - the same
shape [MDLOC005](/sdk/localization/#mdloc005) checks a resource file's culture suffix against.

An **unrecognised** tag is not an error, for the same reason an unknown `permissions` entry is not: this
is a declaration a store reads, never something that can make a plugin unrunnable. A **blank** or
**duplicated** tag is rejected, and duplicates are compared case-insensitively - `de` and `DE` name one
language twice.

**What a running plugin serves is the authority.** This field exists so a store or an update listing can
show a plugin's languages *before* it is installed. Once it is installed and running, the plugin's
localization capability reports the catalog it actually carries, and nothing in the host resolves text
against this list.

## `dependencies`, `conflicts` and `iconPacks`

All three are arrays of `{ id, versionRange?, optional? }`. The reader rejects a duplicated `id` within
any one array, and rejects an id appearing in both `dependencies` and `conflicts`.

| Field | Type | Default | Requirement | Notes |
| --- | --- | --- | --- | --- |
| `id` | string | — | Runtime | **Required.** Reverse-domain id. |
| `versionRange` | string | any version | Recommended | The grammar above. |
| `optional` | boolean | `false` | Recommended | `false` makes a dependency a hard requirement. |

How each is resolved against what is installed:

| Declaration | Condition | Severity | Effect |
| --- | --- | --- | --- |
| `dependencies` entry, `optional: false` | Not installed, or out of range | Blocking | Installs and activates; **not** auto-started. |
| `dependencies` entry, `optional: true` | Not installed, or out of range | Advisory | Installs, activates and starts normally. |
| `conflicts` entry | The named plugin is installed **and** its active version satisfies the conflict's own `versionRange` | Blocking | Installs and activates; **not** auto-started, regardless of its `optional` flag. |
| `iconPacks` entry | Always | Advisory | Parsed and exposed only. |

A Blocking warning never fails the install or the activation - it only withholds the automatic start. A
ranged conflict is narrower than it looks: if the installed version falls outside the declared range,
no warning is produced at all.

`iconPacks` references are always Advisory regardless of `optional`, because icon packs are identified
by a database id minted on import rather than a stable reverse-domain id, so resolution can never
succeed today.

## `files`

Requirement: Generated - produced by the packaging pipeline from the artifact payload, never
hand-authored. Per-file digests covering the artifact payload.

| Field | Type | Requirement | Notes |
| --- | --- | --- | --- |
| `path` | string | Generated | **Required (within the block).** Forward-slash separated, relative to the version directory. Duplicates are rejected, case-insensitively. |
| `sha256` | string | Generated | **Required.** `sha256:` followed by exactly 64 lowercase hex characters. |
| `size` | integer | Generated | **Required.** Expected size in bytes, ≥ 0. |

When `files[]` is present the installer verifies every entry **and rejects any extracted file not
declared there** - once the list exists at all it is a complete inventory, so an undeclared extra file
is as real a mismatch as a wrong hash. When it is absent, the installer records an advisory warning
instead.

You do not write this array by hand. `macrodeck-plugin pack` recomputes it from what is actually on
disk and discards whatever the source manifest declared. A hand-authored `files` in a manifest still
sitting in an unbuilt source tree is not an error - `macrodeck-plugin validate`/`build`/`pack` report it
as a `generated-field-authored` warning, since a packed artifact or an already-extracted install
directory legitimately carries this field.

## `signature`

Requirement: Generated - produced by the packaging/signing pipeline, never hand-authored (see `files`
above for what a hand-authored value in a source tree reports as). The creator signature over the
format's canonical digest - never over re-serialised manifest JSON. Embedded in the manifest itself,
alongside `certificate.json` and `certificate.sig` at the archive root; there is no detached signature
file. On a Store artifact it is written by the Creator Portal when it signs (see
[Publishing to the Store](/guides/publishing/)), and by `macrodeck-plugin sign` on an artifact signed
outside the Store; see the
[package signature schema](/schemas/macrodeck-package-signature-v1.schema.json) for the exact shape.

| Field | Type | Requirement | Notes |
| --- | --- | --- | --- |
| `algorithm` | string | Generated | **Required**, non-blank. Only `ed25519` is understood today. |
| `keyId` | string | Generated | **Required**, non-blank. The signing certificate's `certificateId`. Verification fails when it names a certificate other than the one the artifact carries. |
| `value` | string | Generated | **Required**, non-blank. Base64-encoded signature bytes. |
| `signedAt` | string | Generated | RFC 3339 date-time, when the signer recorded it. The certificate's validity window is evaluated at this instant, so a package signed while its certificate was valid stays verifiable after the certificate expires. |

The host verifies this block cryptographically - before the install is written and again on every launch
- and every outcome except "unsigned" refuses the install. An unsigned artifact stays installable, but
only on an explicit confirmation for a file the user selected or uploaded. A signature that is present
and does not verify is a failure, not a weaker kind of unsigned. `macrodeck-plugin verify` runs the same
check - certificate chain, validity at `signedAt`, the canonical digest and every declared file - and is
what a CI pipeline should gate on before an artifact ever reaches a host. See
[signing](/policies/security/#signing-the-creator-portal-signs-and-the-host-verifies-before-install-and-before-every-load)
for the full verdict table.

**A signature is applied after packing, against the packed artifact's manifest** - by the Creator Portal
for a Store artifact, or by `macrodeck-plugin sign` outside the Store. `pack` recomputes `files[]`, so a
signature applied beforehand no longer matches its own digest. `pack` passes an existing `signature`
through untouched rather than dropping it, and `sign` itself refuses to replace an existing one. See the
CLI guide's [`sign`](/cli/signing/#sign) and [`verify`](/cli/signing/#verify) sections.

## A complete example

```json
{
  "$schema": "https://schemas.macro-deck.app/plugin-manifest-v1.schema.json",
  "manifestVersion": 1,
  "id": "com.example.full",
  "name": "Full Example Plugin",
  "version": "1.2.0",
  "description": "Demonstrates every field a manifest can declare.",
  "icon": "assets/icon.svg",
  "entrypoints": {
    "win-x64": { "executable": "FullPlugin.exe", "arguments": ["--quiet"] },
    "osx-arm64": { "executable": "FullPlugin" },
    "linux-x64": {
      "executable": "FullPlugin.dll",
      "runtime": { "kind": "FrameworkDependent", "dotnetVersion": "10.0" }
    }
  },
  "shutdown": {
    "gracefulTimeoutSeconds": 10
  },
  "health": {
    "path": "/_macrodeck/health",
    "intervalSeconds": 15,
    "timeoutSeconds": 2,
    "unhealthyThreshold": 3
  },
  "publisher": {
    "name": "Example Publisher",
    "id": "com.example",
    "email": "plugins@example.com",
    "url": "https://example.com"
  },
  "license": "MIT",
  "homepage": "https://example.com/full-plugin",
  "repository": "https://github.com/example/full-plugin",
  "compatibility": {
    "sdk": ">=1.0.0,<2.0.0",
    "protocol": { "minimum": 1, "maximum": 1 },
    "macroDeck": ">=3.0.0"
  },
  "permissions": ["host:variables", "net:outbound"],
  "languages": ["en", "de", "zh-Hant-TW"],
  "dependencies": [
    { "id": "com.example.core", "versionRange": ">=1.0.0,<2.0.0", "optional": false },
    { "id": "com.example.extras", "optional": true }
  ],
  "conflicts": [
    { "id": "com.example.legacy" }
  ],
  "iconPacks": [
    { "id": "com.example.icons", "versionRange": ">=1.0.0", "optional": true }
  ],
  "files": [
    {
      "path": "FullPlugin.exe",
      "sha256": "sha256:b512f83e009ab0fcdda47e89a16013fa8bdf0baeb4b10e3343fc7ceca3cf6f3a",
      "size": 204800
    }
  ],
  "signature": {
    "algorithm": "ed25519",
    "keyId": "macro-deck-store-2026",
    "value": "MEUCIQDx8p6...base64...==",
    "signedAt": "2026-01-15T12:00:00Z"
  }
}
```

A minimal manifest is much shorter:

```json
{
  "$schema": "https://schemas.macro-deck.app/plugin-manifest-v1.schema.json",
  "manifestVersion": 1,
  "id": "com.example.minimal",
  "name": "Minimal Plugin",
  "version": "1.0.0",
  "entrypoints": {
    "win-x64": { "executable": "MinimalPlugin.exe" },
    "osx-arm64": { "executable": "MinimalPlugin" },
    "linux-x64": { "executable": "MinimalPlugin" }
  }
}
```

## Checking a manifest

```bash
macrodeck-plugin validate --manifest manifest.json
```

`validate` runs the real manifest reader, the embedded JSON Schema, the permission vocabulary and -
when `files[]` is declared - the digests against real bytes, reporting every independent problem in one
run. Add `--level package` or `--level publication` to also check the requirement categories above -
see [`validate --level`](/cli/validate/). `inspect` describes an artifact without judging
it. See [the CLI guide](/cli/).

The entrypoint keys above are also what [`macrodeck-plugin build`](/cli/build/) treats as the
list of platforms to build: it produces every declared runtime identifier, or one at a time with `--rid`,
and fails if a declared entrypoint does not appear in the build output.

## See also

- [The raw schema](/schemas/plugin-manifest-v1.schema.json) - machine-checkable, with per-field
  descriptions.
- [Plugin hosting](/sdk/hosting/) - the artifact format, the installer's rules and the on-disk
  layout this manifest describes.
- [Plugin CLI](/cli/) - `build`, `validate`, `inspect` and `pack`.
- [Security model](/policies/security/) - what signing and permissions do and do not guarantee.
