---
title: macrodeck-plugin merge
description: Merge single-platform packages built on different machines into one multi-platform package.
---

`merge` combines the packages [`build --rid`](/cli/build/#single-platform-artifacts) produced on different
runners into one `.macroDeckPlugin` declaring every runtime identifier. Use it when each platform has to be
built natively, for example a `net10.0-windows` target on Windows and a native library on macOS.

## Example

```bash
macrodeck-plugin merge \
  artifacts/com.example.spotify-controller-1.0.0-win-x64.macroDeckPlugin \
  artifacts/com.example.spotify-controller-1.0.0-osx-arm64.macroDeckPlugin \
  artifacts/com.example.spotify-controller-1.0.0-linux-x64.macroDeckPlugin \
  --output dist
```

```text
Merged linux-x64, osx-arm64, win-x64.
Packed com.example.spotify-controller 1.0.0 -> dist/com.example.spotify-controller-1.0.0.macroDeckPlugin (...).
```

The result is the package a full `build` of the same commit produces on a machine that can build every
platform.

## Options

| Option | Default | Description |
| --- | --- | --- |
| `<artifacts>...` | required | The packages to merge. |
| `--output <dir>` | `.` | **Directory** for the artifact, named `<id>-<version>.macroDeckPlugin`. |
| `--force` | off | Overwrite an existing artifact. |

## What merge checks

Nothing is written unless every check passes.

- **Each package is intact.** Every file must match the digest its own manifest lists, and no file may be
  unlisted (`hash-mismatch`), since packages usually travel between CI jobs.
- **Same plugin, same version** (`identity-mismatch`).
- **Same manifest apart from `entrypoints`** (`manifest-mismatch`). Build every package from the same commit
  with the same version.
- **Each runtime identifier appears once** (`duplicate-rid`).
- **Shared files are identical.** A file outside a runtime identifier's own directory, such as the icon or an
  `include`d asset, may appear in several packages but must have the same bytes in each (`file-conflict`).

Like `build`, `merge` never signs: any `signature` is dropped and `files[]` is recomputed.

## Exit codes

| Code | When |
| --- | --- |
| 0 | The artifact was written. |
| 1 | A package is invalid or the packages do not belong together. |
| 2 | Bad arguments, or an artifact that already exists without `--force`. |
| 3 | A package could not be found or is not a ZIP archive. |
| 4 | Cancelled (Ctrl-C). |
| 70 | Staging failed. |

## See also

- [`build`](/cli/build/) - `--rid` produces the packages merged here.
- [CI and automation](/cli/ci/) - a matrix workflow that merges its results.
