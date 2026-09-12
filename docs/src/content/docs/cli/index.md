---
title: Plugin CLI
description: 'macrodeck-plugin: scaffold, build, validate, inspect, pack, run, test and sign a Macro Deck plugin without installing a host.'
---

`macrodeck-plugin` scaffolds, builds, checks, runs and packages a Macro Deck plugin without Macro Deck
installed.

## Installing

```bash
dotnet tool install --global MacroDeck.Plugin.Cli --prerelease
```

- `--prerelease` is required until a stable 3.0 build ships: only `-preview` versions are published, and
  `dotnet tool install` skips prereleases unless asked.
- The tool needs the **ASP.NET Core shared framework**, not just the .NET runtime, because `run` and
  `test` start a real Kestrel loopback host (via
  [`MacroDeck.Plugin.Testing`](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/sdk/src/MacroDeck.Plugin.Testing/README.md)).
  If they report that `Microsoft.AspNetCore.App` is missing, install the ASP.NET Core runtime or SDK.

## Typical workflow

```bash
macrodeck-plugin new --name "Spotify Controller" --publisher "Example Publisher" --yes
cd SpotifyController/src/SpotifyController
macrodeck-plugin run --project SpotifyController.csproj --stub-host
macrodeck-plugin build --output ../../artifacts
macrodeck-plugin validate --artifact ../../artifacts/com.example.spotify-controller-1.0.0.macroDeckPlugin
```

The artifact `build` (or `pack`) produces is what your publishing workflow submits to the Creator Portal,
which signs it - see [Publishing to the Store](/guides/publishing/). There is no `keygen` or `sign` step on
the way to the Store: those commands are for artifacts distributed outside the Store and for Macro Deck's
own infrastructure.

## Commands

| Command | What it does |
| --- | --- |
| [`new`](/cli/new/) | Scaffold a plugin project from the official template. |
| [`build`](/cli/build/) | Build every runtime identifier the manifest declares and package the result. |
| [`validate`](/cli/validate/) | Validate a manifest, a version directory or a packed artifact. |
| [`inspect`](/cli/inspect/) | Report what installing an artifact would find, without a running host. |
| [`pack`](/cli/pack/) | Pack an existing payload directory into a `.macroDeckPlugin` artifact. |
| [`run`](/cli/run/) | Run a plugin against the running host or a disposable stub host. |
| [`test`](/cli/test/) | Run the [conformance suite](/reference/conformance/) and write a text, JSON or Markdown report. |
| [`keygen` / `sign` / `verify`](/cli/signing/) | Creator key pairs and package signatures, for artifacts distributed outside the Store. |

Run `macrodeck-plugin <command> --help` for a command's options.

## Global options

These work on every command, before or after the command name:

| Option | Default | Description |
| --- | --- | --- |
| `--verbosity <quiet\|normal\|diagnostic>` | `normal` | How much a command narrates while it works. |
| `--no-color` | off | Disable ANSI colour in text output. |

`quiet` hides progress lines only. A command's result - a validation report, a conformance report, a
plugin's own console output, any `error` or `warning` line - always prints.

`--output` is not global. `validate` and `inspect` use it for a render format (`text`/`json`), `pack` and
`test` for a destination file, and `new`, `build` and `keygen` for a destination directory.

## Errors and warnings

```text
$ macrodeck-plugin build
error manifest-not-found: No manifest at '~/src/SpotifyController/manifest.json'.
```

- Failures print one line on **stderr**: `error <kebab-case-code>: <sentence>`, red unless `--no-color`
  is given.
- Non-fatal observations (a missing foreign-RID entrypoint, a Debug-looking source tree, missing
  publication metadata) print the same way as `warning <code>: <sentence>`, also on stderr. A warning
  **never changes the exit code**.
- `validate` is the exception: its `error`/`warning` lines are its result, so they go to **stdout** and
  `validate ... > report.txt` captures them.
- Paths in messages are always resolved and absolute.

Running with no arguments prints the command list (exit 2). A mistyped command gets a suggestion when one
command name is close, otherwise a pointer to `--help`:

```text
$ macrodeck-plugin pakc --source .
error unknown-command: 'pakc' is not a macrodeck-plugin command. Did you mean 'pack'?
```

## Exit codes

| Code | Meaning |
| --- | --- |
| 0 | Success, or conformant. |
| 1 | The subject is wrong: validation failed, or a required conformance check failed. |
| 2 | Usage error: bad arguments, an unknown `--check`/`--category` token, or an output file that already exists without `--force`. |
| 3 | The input could not be read: a missing file, something that is not a ZIP, or a permissions failure. |
| 4 | Cancelled (Ctrl-C). |
| 70 | An error the command did not anticipate. |

1 and 3 are kept apart so CI can tell a broken plugin from a broken environment. [`run`](/cli/run/) is
the one exception on a normal exit: it returns the launched plugin's own exit code.

## See also

- [Testing plugins](/features/testing/) - `MacroDeck.Plugin.Testing`, which `run` and `test` are built on.
- [Conformance](/reference/conformance/) - the suite `test` runs and its report shape.
- [Plugin hosting](/reference/plugin-hosting/) - the artifact format `pack`/`validate`/`inspect` read, and
  what the supervisor injects that `run` reproduces.
- [Publishing to the Store](/guides/publishing/) - how a plugin is published and signed.
- [Security model](/policies/security/) - the trust model `verify` checks against.
- [`MacroDeck.Signing` package README](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/sdk/src/MacroDeck.Signing/README.md) -
  the library behind `sign` and `verify`.
- [Certificate schema](/schemas/macrodeck-certificate-v1.schema.json) and
  [package signature schema](/schemas/macrodeck-package-signature-v1.schema.json).
- [`MacroDeck.Plugin.Cli` package README](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/sdk/src/MacroDeck.Plugin.Cli/README.md).
