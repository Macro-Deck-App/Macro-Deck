---
title: Manifest reference
description: Every field of a Macro Deck plugin manifest.json - types, required-ness, patterns, defaults and clamped ranges - with a complete worked example.
---

`manifest.json` sits at the root of a `.macroDeckPlugin` artifact, at the root of an installed version
directory and, during development, at the plugin's content root, where the SDK reads it at startup.

## Example

What `macrodeck-plugin new` generates:

```json
{
  "$schema": "https://schemas.macro-deck.app/plugin-manifest-v1.schema.json",
  "manifestVersion": 1,
  "id": "com.example.hue-lights",
  "name": "Hue Lights",
  "version": "1.0.0",
  "description": "Control Hue lights from Macro Deck.",
  "icon": "Assets/icon.svg",
  "entrypoints": {
    "win-x64": { "executable": "runtimes/win-x64/HueLights.exe" },
    "osx-arm64": { "executable": "runtimes/osx-arm64/HueLights" },
    "linux-x64": { "executable": "runtimes/linux-x64/HueLights" }
  },
  "publisher": { "name": "Example Publisher" },
  "license": "MIT",
  "compatibility": { "macroDeck": ">=3.0.0" },
  "repository": "https://github.com/example/hue-lights",
  "homepage": "https://example.com/hue-lights"
}
```

- Schema: [`plugin-manifest-v1.schema.json`](/schemas/plugin-manifest-v1.schema.json)
  (`$id` `https://schemas.macro-deck.app/plugin-manifest-v1.schema.json`). Name it in `$schema` for editor
  validation and completion.
- Property names are camelCase.
- **Unknown properties are ignored everywhere**, so an older host reads a newer manifest as it did before.
  Do not validate with a schema that rejects unknown properties; the host accepts them.

## Fields

Requirement is the [category](#requirement-categories) that decides when a field is required.

| Field | Type | Requirement | Meaning |
| --- | --- | --- | --- |
| `manifestVersion` | integer | runtime | Must be exactly `1`. |
| `id` | string | runtime | Reverse-domain plugin id; must match the install directory. |
| `name` | string | runtime | Display name, 1-128 characters, no control characters. |
| `version` | string | runtime | SemVer 2.0; must match the install directory. |
| `entrypoints` | object | runtime | At least one launch target, keyed by runtime identifier. |
| `description` | string | publication | Human-readable description. |
| `icon` | string | publication | Relative path to the plugin icon. |
| `publisher` | object | publication | The Creator or Organization account that owns the listing. |
| `license` | string | publication | SPDX identifier by convention, carried verbatim and never parsed. |
| `repository` | string | publication | Absolute `http`/`https` URL. Informational. |
| `compatibility` | object | publication | SDK, protocol and Macro Deck ranges the plugin supports. |
| `homepage` | string | recommended | Absolute `http`/`https` URL. Informational. |
| `shutdown` | object | recommended | Graceful shutdown timeout. |
| `health` | object | recommended | Health probe settings. |
| `permissions` | string[] | recommended | Host capabilities the plugin declares it uses. |
| `languages` | string[] | recommended | BCP-47 tags of the plugin's own strings. Normally derived by the tooling. |
| `dependencies` | object[] | recommended | Plugins this one needs. |
| `conflicts` | object[] | recommended | Plugins this one cannot run beside. |
| `iconPacks` | object[] | recommended | Icon packs this one references. |
| `files` | object[] | generated | Per-file digests of the payload. Written by `pack`. |
| `signature` | object | generated | Creator signature. Written by the signer. |

The five `runtime` fields are the schema's top-level `required` array.

## Identity

```json
"manifestVersion": 1,
"id": "com.example.hue-lights",
"name": "Hue Lights",
"version": "1.0.0"
```

- `manifestVersion` other than `1` is rejected before the rest is parsed, so a future manifest reports
  exactly that (`unsupported-manifest-version`).
- `name`: the no-control-characters rule is enforced by the host; the schema cannot express it.
- `id` and `version` locate the install: `<dataRoot>/plugins/<id>/versions/<version>/`. A manifest whose
  `id` or `version` disagrees with that directory is rejected.

### Package-ID rules

```
^[a-z][a-z0-9]*(-[a-z0-9]+)*(\.[a-z][a-z0-9]*(-[a-z0-9]+)*)+$
```

| Rule | Valid | Invalid |
| --- | --- | --- |
| Lowercase letters, digits, hyphens, dots only | `com.example.hue-lights` | `com.Example.hue_lights` |
| At least two dot-separated segments | `com.example` | `myplugin` |
| Each segment starts with a letter; hyphens only between letters/digits | `my-plugin2` | `-my`, `my--plugin`, `my-` |
| At most 128 characters | | |

The same pattern validates `publisher.id` and every `id` in `dependencies`, `conflicts` and `iconPacks`.
Because an underscore is illegal, the `_staging` and `_cache` directories can never be taken for a plugin.

## Entrypoints

```json
"entrypoints": {
  "win-x64": { "executable": "runtimes/win-x64/HueLights.exe", "arguments": ["--quiet"] },
  "osx-arm64": { "executable": "runtimes/osx-arm64/HueLights" },
  "linux-x64": {
    "executable": "runtimes/linux-x64/HueLights.dll",
    "runtime": { "kind": "FrameworkDependent", "dotnetVersion": "10.0" }
  }
}
```

| Field | Type | Default | Requirement | Rules |
| --- | --- | --- | --- | --- |
| `executable` | string | - | runtime | Relative to the version directory and inside it: no `..`, no absolute path. Never a script: `.sh`, `.bat`, `.cmd`, `.ps1`, `.command` are rejected whatever the `runtime`. |
| `arguments` | string[] | none | recommended | Passed to the executable in order. |
| `runtime.kind` | string | `SelfContained` | recommended | `SelfContained` or `FrameworkDependent`, case-insensitive. Omitting `runtime` means self-contained. |
| `runtime.dotnetVersion` | string | - | recommended | `major.minor`, e.g. `"10.0"`. Required when `kind` is `FrameworkDependent`, ignored otherwise. |

Reader-only cross-field rule: `FrameworkDependent` requires `executable` to end in `.dll`; self-contained
requires it not to.

The `runtimes/<rid>/` layout that `build` and `pack` expect is described once, in
[Staging layout](/cli/build/#staging-layout). `build` and `validate --level package` judge it with one
shared rule (`entrypoint-layout-invalid`). `build` treats the entrypoint keys as the list of platforms to
build (or one with `--rid`) and fails when a declared entrypoint is missing from the output.

### Runtime identifier resolution

| Host RID | Uses |
| --- | --- |
| Exact key present | That key |
| `osx-arm64` without its own key | `osx-x64` |
| `win-arm64` without its own key | `win-x64` |
| Anything else, including `linux-musl-*` | Nothing - there is no `"any"` key and no other fallback |

An artifact with no entrypoint for the host still installs and activates; the installer adds an advisory
warning and does not start it. Only the entrypoint for the current RID is checked for existence on disk.

## Icon

```json
"icon": "Assets/icon.svg"
```

- Forward-slash path relative to the version directory: no `..` segment, no absolute path.
- Lets a listing show an icon for a plugin that is not running; a running plugin's icon still travels over
  the asset pipeline.
- Not checked for existence at Development level, so a missing icon never stops a working plugin. At
  Package level and above, `validate`, `build` and `pack` report `icon-declared-not-present`.
- When `files[]` is present, list the icon there like any other payload file.

## Timing settings

```json
"shutdown": { "gracefulTimeoutSeconds": 10 },
"health": {
  "path": "/_macrodeck/health",
  "intervalSeconds": 15,
  "timeoutSeconds": 2,
  "unhealthyThreshold": 3
}
```

Numeric values are **clamped, never rejected**.

| Field | Type | Default | Clamped to | Meaning |
| --- | --- | --- | --- | --- |
| `shutdown.gracefulTimeoutSeconds` | integer | 10 | 1-60 | Time to exit after `session.goodbye` and the WebSocket close, before a process-tree kill. |
| `health.path` | string | `/_macrodeck/health` | - | SDK health route the probe calls. |
| `health.intervalSeconds` | integer | 15 | 5-120 | Seconds between probes. |
| `health.timeoutSeconds` | integer | 2 | 1-10 | Seconds before one probe counts as failed. |
| `health.unhealthyThreshold` | integer | 3 | 2-10 | Consecutive failures before the plugin is unhealthy. The floor of 2 means one missed probe never restarts a plugin. |

## `permissions`

```json
"permissions": ["host:variables", "net:outbound"]
```

Recommended. Array of unique strings. Known vocabulary, one per host callback surface:

| Group | Permissions |
| --- | --- |
| Host | `host:variables`, `host:user-variables`, `host:config`, `host:deck`, `host:scripts`, `host:widgets`, `host:notifications`, `host:action-interactions`, `host:devices`, `host:variable-values`, `host:layouts`, `host:folder-views`, `host:widget-types` |
| Other | `events:publish`, `assets:upload`, `net:outbound`, `fs:user-files`, `process:spawn`, `device:usb` |

- Unknown permission: warning (`unknown-permission`), still installs. Not an enum on purpose.
- Duplicate: rejected.
- **Declared, validated, persisted and exposed - enforced nowhere.** See
  [the security model](/policies/security/#permissions-declared-not-enforced).

## `languages`

```json
"languages": ["en", "de", "zh-Hant-TW"]
```

Recommended. Array of unique [BCP-47](https://www.rfc-editor.org/info/bcp47) tags.

- **Normally not hand-written.** `build` and `pack` derive it from `Localization/*.resx`: unsuffixed
  `Strings.resx` is `en`, each culture-suffixed sibling adds its tag. A hand-written value survives only
  when there is nothing to derive from; when both exist and disagree, the derived list wins and `pack` says
  so. See [the manifest languages field](/features/localization/#the-manifest-languages-field).
- Full tags, never truncated: `zh-Hans`/`zh-Hant` and `pt-BR`/`pt-PT` stay distinct.
- Shape: 2-3 letter language, optional 4-letter script, optional 2-letter or 3-digit region - the shape
  [MDLOC005](/features/localization/#mdloc005) checks.
- Unrecognised tag: not an error. Blank or duplicate (case-insensitive, `de` = `DE`): rejected.
- For stores and update listings before install only. A running plugin's localization capability is the
  authority; nothing in the host resolves text against this list.

## Publication metadata

```json
"description": "Control Hue lights from Macro Deck.",
"license": "MIT",
"repository": "https://github.com/example/hue-lights",
"homepage": "https://example.com/hue-lights",
"publisher": {
  "name": "Example Publisher",
  "id": "com.example",
  "email": "plugins@example.com",
  "url": "https://example.com"
}
```

`homepage`, `repository` and `publisher.url` must be absolute `http`/`https` URLs.

### `publisher`

| Field | Type | Requirement | Rules |
| --- | --- | --- | --- |
| `name` | string | publication | Required, non-empty, whenever the block is present. |
| `id` | string | recommended | Reverse-domain id when present. |
| `email` | string | recommended | Contact email. |
| `url` | string | recommended | Absolute `http`/`https` URL. |

Locally `publisher` is an unchecked claim. At upload the Creator Portal checks that `publisher.name`
matches the authenticated Creator/Organization account and that `publisher.id`, **when present**, is that
account's id - so `publisher.id` stays recommended. Signing, not this field, authenticates an artifact:
see [signature](#signature), [Publishing to the Store](/guides/publishing/) and
[the security model](/policies/security/).

### `compatibility`

```json
"compatibility": {
  "sdk": ">=1.0.0,<2.0.0",
  "protocol": { "minimum": 1, "maximum": 1 },
  "macroDeck": ">=3.0.0"
}
```

The block is publication-required: declare at least one member. An absent member declares nothing, never
"incompatible".

| Field | Type | Requirement | At install |
| --- | --- | --- | --- |
| `sdk` | version range | recommended | Recorded, **not** checked - the host learns the SDK only on connect. |
| `protocol` | `{ minimum, maximum }` | recommended | Outside the host's range **rejects**. Inclusive range of protocol majors; `minimum` >= 1, `maximum` >= `minimum`, both runtime-required once `protocol` is present. Same type as the wire handshake. |
| `macroDeck` | version range | recommended | Outside the range **rejects**. Skipped on a development host build (`0.0.0-dev`). |

A failed compatibility check is a rejection, never a warning.

### Version range grammar

Used by `compatibility.sdk`, `compatibility.macroDeck` and every `versionRange`:

```
*                      any version
>=1.2.0                one comparator
>=1.2.0,<2.0.0         comma means AND
```

- Comparators: `=`, `>`, `>=`, `<`, `<=`. **No caret, tilde, `||` or wildcard.**
- Plain SemVer 2.0 precedence: `>=1.0.0` is not satisfied by `1.0.0-beta.1`.
- Whitespace around comparators and commas is trimmed: `">= 1.0.0, < 2.0.0"` is accepted.

## Dependencies

```json
"dependencies": [
  { "id": "com.example.core", "versionRange": ">=1.0.0,<2.0.0" },
  { "id": "com.example.extras", "optional": true }
],
"conflicts": [{ "id": "com.example.legacy" }],
"iconPacks": [{ "id": "com.example.icons", "versionRange": ">=1.0.0", "optional": true }]
```

`dependencies`, `conflicts` and `iconPacks` are arrays of:

| Field | Type | Default | Requirement |
| --- | --- | --- | --- |
| `id` | string | - | runtime - reverse-domain id |
| `versionRange` | string | any version | recommended - [grammar](#version-range-grammar) |
| `optional` | boolean | `false` | recommended - `false` makes a dependency hard |

A duplicate `id` within one array, or an `id` in both `dependencies` and `conflicts`, is rejected.

| Declaration | When | Severity | Effect |
| --- | --- | --- | --- |
| dependency, `optional: false` | Missing or out of range | Blocking | Installs and activates; **not** auto-started. |
| dependency, `optional: true` | Missing or out of range | Advisory | Installs, activates, starts. |
| conflict | Named plugin installed **and** its active version satisfies the conflict's `versionRange` | Blocking | Installs and activates; **not** auto-started, whatever `optional` says. Outside the range: no warning at all. |
| icon pack | Always | Advisory | Parsed and exposed only - icon packs have database ids minted on import, so resolution cannot succeed today. |

Blocking never fails install or activation; it only withholds the automatic start.

## `files`

```json
"files": [
  {
    "path": "runtimes/win-x64/HueLights.exe",
    "sha256": "sha256:b512f83e009ab0fcdda47e89a16013fa8bdf0baeb4b10e3343fc7ceca3cf6f3a",
    "size": 204800
  }
]
```

Generated by `macrodeck-plugin pack`, which recomputes it from disk and discards any authored value.

| Field | Type | Rules |
| --- | --- | --- |
| `path` | string | Required. Forward-slash, relative to the version directory. Duplicates rejected case-insensitively. |
| `sha256` | string | Required. `sha256:` plus exactly 64 lowercase hex characters. |
| `size` | integer | Required. Bytes, >= 0. |

- Present: the installer verifies every entry **and rejects any extracted file not listed**.
- Absent: the installer records an advisory warning.
- Hand-authored in an unbuilt source tree: `validate`, `build` and `pack` warn `generated-field-authored`
  (a packed artifact or extracted install legitimately carries it).

## `signature`

```json
"signature": {
  "algorithm": "ed25519",
  "keyId": "macro-deck-store-2026",
  "value": "MEUCIQDx8p6...base64...==",
  "signedAt": "2026-01-15T12:00:00Z"
}
```

Generated: written by the Creator Portal for a Store artifact, or by `macrodeck-plugin sign` outside the
Store. Signs the format's canonical digest, never re-serialised manifest JSON. Embedded here, with
`certificate.json` and `certificate.sig` at the archive root; no detached signature file. Exact shape:
[package signature schema](/schemas/macrodeck-package-signature-v1.schema.json).

| Field | Type | Rules |
| --- | --- | --- |
| `algorithm` | string | Required, non-blank. Only `ed25519` is understood. |
| `keyId` | string | Required, non-blank. The signing certificate's `certificateId`; verification fails if it names another certificate than the one carried. |
| `value` | string | Required, non-blank. Base64 signature bytes. |
| `signedAt` | string | RFC 3339 date-time. The certificate's validity is evaluated at this instant, so a package stays verifiable after its certificate expires. |

- The host verifies before writing the install and on every launch. Every outcome except "unsigned"
  refuses; unsigned installs only on explicit confirmation for a file the user selected or uploaded. A
  present signature that does not verify is a failure, not a weaker unsigned.
- Sign **after** packing: `pack` recomputes `files[]`, so an earlier signature no longer matches. `pack`
  passes an existing `signature` through untouched; `sign` refuses to replace one.
- `macrodeck-plugin verify` runs the same check (chain, validity at `signedAt`, canonical digest, every
  declared file) - gate CI on it. See [`sign`](/cli/signing/#sign), [`verify`](/cli/signing/#verify) and the
  [signing verdict table](/policies/security/#signing-the-creator-portal-signs-and-the-host-verifies-before-install-and-before-every-load).

## Requirement categories

| Category | Required when |
| --- | --- |
| `runtime` | Always. The host's reader rejects a manifest missing one. |
| `publication` | Before acceptance into the public Macro Deck plugin ecosystem. Never for local development. |
| `recommended` | Never. |
| `generated` | Never hand-authored; produced by `build`/`pack`/signing (`files`, `signature`). |

Categories feed three cumulative levels, `Development ⊂ Package ⊂ Publication`. A manifest valid at a
level stays valid at that level.

| Level | Checks | Run by |
| --- | --- | --- |
| Development | Every `runtime` field, manifest/entrypoint/compatibility shape. Never gets stricter. | The host at install - the only level it enforces. `validate --manifest`/`--directory` by default. |
| Package | Development, plus packaged content (entrypoints and icon present, valid multi-RID layout), plus missing `publication` fields as **warnings**. | `build`, `pack`, `validate --level package`; `validate --artifact` by default. |
| Publication | Package, with missing `publication` fields as **errors**. | `validate --level publication`, the Creator Portal at upload. |

Each schema property carries a non-standard `x-macrodeck-requirement` annotation (`"runtime"`,
`"publication"`, `"recommended"` or `"generated"`). Validators ignore it; tooling can read categories from
[the schema](/schemas/plugin-manifest-v1.schema.json) instead of hardcoding this table.
`MacroDeck.Plugin.Packaging.Manifest.PluginManifestRequirements` reads the same annotations from the
schema embedded in that package.

See [`validate --level`](/cli/validate/), [Publishing to the Store](/guides/publishing/) and
[Plugin hosting](/reference/plugin-hosting/).

## Validation

```bash
macrodeck-plugin validate --manifest manifest.json
macrodeck-plugin validate --manifest manifest.json --level publication
```

`validate` runs the real reader, the embedded schema, the permission vocabulary and, when `files[]` is
present, the digests against real bytes, reporting every independent problem in one run. `inspect`
describes an artifact without judging it. See [the CLI guide](/cli/).

Actual output for broken variants of the example:

| Change | Diagnostic |
| --- | --- |
| `"manifestVersion": 2` | `error unsupported-manifest-version: Manifest version 2 is not supported; only 1 is understood.` |
| `"id": "MyPlugin"` | `error invalid-plugin-id: 'MyPlugin' is not a valid plugin id.` |
| `name` removed | `error schema:required: Required properties ["name"] are not present []` |
| `"version": "1.0"` | `error invalid-version: '1.0' is not a valid SemVer version. [/version]` |
| `"entrypoints": {}` | `error no-entrypoints: Manifest declares no entrypoints.` |
| `"executable": "../x"` | `error entrypoint-outside-version-directory: Entrypoint 'linux-x64' executable '../x' escapes the version directory.` |
| `"executable": "run.sh"` | `error invalid-entrypoint-runtime: Entrypoint 'linux-x64' executable 'run.sh' looks like a script, which the artifact format forbids.` |
| `FrameworkDependent`, executable `x` | `error invalid-entrypoint-runtime: Entrypoint 'linux-x64' is framework-dependent but its executable does not end in '.dll'.` |
| No `runtime`, executable `x.dll` | `error invalid-entrypoint-runtime: Entrypoint 'linux-x64' is self-contained but its executable ends in '.dll'.` |
| `"icon": "../icon.svg"` | `error invalid-icon: Icon path '../icon.svg' is not a safe relative path.` |
| Missing icon file, `--level package` | `error icon-declared-not-present: 'Assets/nope.svg' is declared as 'icon' but is not present in the packaged content. [/icon] (package)` |
| `"homepage": "example.com"` | `error schema:pattern: The string value is not a match for the indicated regular expression [/homepage]` |
| Permission listed twice | `error invalid-permission: Permission 'host:variables' is declared more than once.` |
| `"permissions": ["host:teleport"]` | `warning unknown-permission: 'host:teleport' is not a known permission. [/permissions/0]` |
| `"languages": ["de", "DE"]` | `error invalid-language: Language 'DE' is declared more than once.` |
| `"macroDeck": "^3.0.0"` | `error invalid-compatibility: Compatibility.MacroDeck '^3.0.0' is not a valid version range.` |
| `"protocol": { "minimum": 2, "maximum": 1 }` | `error invalid-compatibility: Compatibility.Protocol range [2, 1] is invalid.` |
| `"protocol": { "minimum": 1 }` | `error schema:required: Required properties ["maximum"] are not present [/compatibility/protocol]` |
| Same id in `dependencies` and `conflicts` | `error invalid-dependency: 'com.example.core' cannot be both a dependency and a conflict.` |
| `"publisher": { "id": "com.example" }` | `error schema:required: Required properties ["name"] are not present [/publisher]` |
| `description` removed, `--level package` | `warning publication-metadata-missing: 'description' is required to publish to the Macro Deck plugin ecosystem. It is not required to develop or run this plugin locally. [/description] (publication)` |
| `compatibility` removed, `--level publication` | `error publication-metadata-missing: 'compatibility' is required to publish to the Macro Deck plugin ecosystem: declare at least one of 'sdk', 'protocol' or 'macroDeck'. ...` |
| `"health": { "unhealthyThreshold": 1 }` | No diagnostic - clamped to 2. |

## See also

- [The raw schema](/schemas/plugin-manifest-v1.schema.json) - machine-checkable, with per-field
  descriptions.
- [Plugin hosting](/reference/plugin-hosting/) - the artifact, the installer's rules and the on-disk layout.
- [Plugin CLI](/cli/) - `build`, `validate`, `inspect` and `pack`.
- [Security model](/policies/security/) - what signing and permissions do and do not guarantee.
