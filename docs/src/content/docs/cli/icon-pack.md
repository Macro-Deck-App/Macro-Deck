---
title: macrodeck-plugin icon-pack
description: Bundle icon packs with a plugin project, list the bundled packs and remove them again.
---

`icon-pack` manages the icon packs a plugin ships inside its own package. A bundled pack is declared in the
manifest's [`bundledIconPacks`](/reference/manifest/#bundled-icon-packs) under a key that is unique within
the plugin, such as `logos`. The host imports it as a read-only pack owned by the plugin, and the plugin
addresses its icons by key and icon name.

`icon-pack` edits the plugin project, never a built artifact. [`build`](/cli/build/) and
[`pack`](/cli/pack/) then package the pack file like any other payload file, so the plugin's signature
covers it.

The group is named `icon-pack` because `pack` already means "build the artifact" in this CLI.

## Example

```bash
macrodeck-plugin icon-pack add ~/Downloads/service-logos.macroDeckIconPack
```

```text
Copied the pack to 'icon-packs/service-logos.macroDeckIconPack'.
Added icon pack 'Service Logos' (12 icon(s)) under the key 'service-logos'.
```

```bash
macrodeck-plugin icon-pack list
macrodeck-plugin icon-pack remove service-logos
```

Run `add` once per pack. A plugin can bundle up to 32 packs.

## add

```text
macrodeck-plugin icon-pack add <path.macroDeckIconPack> [--key <key>] [--copy] [--force]
```

`add` validates the pack, places it at `icon-packs/<key>.macroDeckIconPack` and records it in the
manifest's `bundledIconPacks`. The rest of `manifest.json`, including property order and any property the
CLI does not know, is kept, and the file keeps its indentation.

Where the pack comes from decides what happens to the file:

| Source | What `add` does |
| --- | --- |
| Outside the project | Copies it. |
| Inside the project | Moves it, so the pack never sits in the project twice, and prints where it moved it from. `--copy` keeps the original in place. |
| Already at `icon-packs/<key>.macroDeckIconPack` | Uses it as it is. |

The pack must pass these checks, or nothing is changed:

- It is a readable `.macroDeckIconPack` archive with a `pack.json` (`icon-pack-invalid`) and fits in one
  plugin artifact entry (`icon-pack-too-large`).
- Every icon has a master image, and every icon name is unique (compared case-insensitively) and usable as
  an address: not blank, no surrounding whitespace, no `/`, no control characters, at most 128 characters
  (`icon-pack-names-invalid`, which lists every offending name).
- A pack that declares AI-generated assets needs the plugin to declare them too: set `ai.generatedAssets`
  to `true` in `manifest.json` first (`ai-declaration-mismatch`). A pack that declares nothing about AI is
  added with the warning `icon-pack-ai-undeclared`, because its icons ship as part of the plugin.

| Option | Default | Description |
| --- | --- | --- |
| `<path>` | required | The `.macroDeckIconPack` file to bundle. |
| `--key <key>` | slug of the pack name | The key the plugin addresses the pack by: lowercase letters, digits and inner hyphens, at most 64 characters. `Service Logos` becomes `service-logos`. When the name yields no key, pass one. |
| `--copy` | off | Copy a pack that is already inside the project instead of moving it. |
| `--force` | off | Replace a pack already bundled under the key: its file and its manifest entry. Without it, `add` refuses an existing key (`icon-pack-exists`). |
| `--source <dir>` | `.` | The plugin project directory. Bundled pack paths are relative to it. |
| `--manifest <path>` | `<source>/manifest.json` | The manifest to edit. |

## list

```text
macrodeck-plugin icon-pack list [--output text|json]
```

```text
logos: icon-packs/logos.macroDeckIconPack - Service Logos, 12 icon(s)
status: icon-packs/status.macroDeckIconPack - missing
```

Lists every declared pack with its key, path, pack name, icon count and whether the file is there.
`--output json` prints the same as a stable shape:

```json
{
  "bundledIconPacks": [
    {
      "key": "logos",
      "path": "icon-packs/logos.macroDeckIconPack",
      "present": true,
      "name": "Service Logos",
      "iconCount": 12,
      "problem": null
    }
  ]
}
```

`name` and `iconCount` are `null` when the file is missing or unreadable; `problem` then says why it could
not be read.

## remove

```text
macrodeck-plugin icon-pack remove <key>
```

Removes the entry from `bundledIconPacks` and deletes the file at its declared path, as long as that path is
inside the project. An unknown key is an error (`icon-pack-not-declared`).

`list` and `remove` take `--source` and `--manifest` like `add`.

## During development

[`run`](/cli/run/#bundled-icon-packs) against a real host syncs the bundled packs when the session starts,
and with `--watch` syncs an `add`, `remove` or replaced pack file right away, without restarting the plugin.

## Exit codes

| Code | When |
| --- | --- |
| 0 | The manifest was updated, or the list was printed. |
| 1 | The pack fails a check, the key is taken or unknown, or the manifest already declares 32 packs. |
| 2 | Bad arguments, including an invalid `--key`. |
| 3 | The project, the manifest or the pack file could not be found or read. |
| 70 | An error the command did not anticipate. |

## See also

- [Manifest reference: bundled icon packs](/reference/manifest/#bundled-icon-packs)
- [`inspect`](/cli/inspect/) - lists the bundled packs of a built artifact.
