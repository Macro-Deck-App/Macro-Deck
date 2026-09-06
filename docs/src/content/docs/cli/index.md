---
title: Plugin CLI
description: 'macrodeck-plugin: scaffold, build, validate, inspect, pack, run, test and sign a Macro Deck plugin without installing a host.'
---

`macrodeck-plugin` is the `MacroDeck.Plugin.Cli` dotnet tool: validate a manifest, inspect, pack or
verify a `.macroDeckPlugin` (or other signable) artifact, run a plugin against the running host or a
disposable stub, and run the [conformance suite](/sdk/conformance/) against it - all without installing Macro
Deck itself.

**Nothing here is how a plugin gets signed for the Store.** Store artifacts are signed by the Creator
Portal, server-side, after it verifies the publishing workflow - see
[Publishing to the Store](/guides/publishing/). The [`keygen`](/cli/signing/#keygen) and [`sign`](/cli/signing/#sign) commands exist for artifacts
distributed outside the Store and for Macro Deck's own infrastructure; they are not a step in normal
plugin development.

## Commands

| Command | What it does |
| --- | --- |
| [`new`](/cli/new/) | Scaffold a plugin project from the official template, interactively or from a script. |
| [`build`](/cli/build/) | Build every runtime identifier the manifest declares and package the result. |
| [`validate`](/cli/validate/) | Validate a manifest, a version directory or a packed artifact. |
| [`inspect`](/cli/inspect/) | Report what installing an artifact would find, without a running host. |
| [`pack`](/cli/pack/) | Pack an existing payload directory into a `.macroDeckPlugin` artifact. |
| [`run`](/cli/run/) | Run a plugin against a disposable stub host, or a real one. |
| [`test`](/cli/test/) | Run the conformance suite and write a text, JSON or Markdown report. |
| [`keygen` / `sign` / `verify`](/cli/signing/) | Creator key pairs and package signatures, for artifacts distributed outside the Store. |

Run `macrodeck-plugin <command> --help` for a command's own options.

## The whole developer flow


```bash
# Scaffold a new plugin, interactively.
macrodeck-plugin new

# The same thing without prompts, e.g. from a script.
macrodeck-plugin new --name "Spotify Controller" --id com.example.spotify-controller \
  --publisher "Example Publisher" --repository https://github.com/example/spotify-controller \
  --platform win-x64 --platform osx-arm64 --platform linux-x64 --yes

# Build every platform the manifest declares and package the result.
macrodeck-plugin build --output ./artifacts

# Build just one platform, e.g. from a CI matrix job.
macrodeck-plugin build --rid win-x64 --output ./artifacts

# Validate a manifest sitting next to your build output.
macrodeck-plugin validate --manifest bin/Release/net10.0/manifest.json

# Pack it into a distributable artifact.
macrodeck-plugin pack --source bin/Release/net10.0 --output MyPlugin-1.0.0.macroDeckPlugin

# See exactly what installing that artifact would find.
macrodeck-plugin inspect --artifact MyPlugin-1.0.0.macroDeckPlugin

# Run it against the running Macro Deck host, which the CLI discovers on its own.
macrodeck-plugin run --project MyPlugin.csproj

# Or against a disposable stub host - no Macro Deck installation required.
macrodeck-plugin run --project MyPlugin.csproj --stub-host

# List every conformance check without running any of them.
macrodeck-plugin test --list-checks

# Run the conformance suite against the packed artifact and write a Markdown report.
macrodeck-plugin test --artifact MyPlugin-1.0.0.macroDeckPlugin --report markdown --output conformance.md

# Verify a signed artifact, e.g. as a CI gate.
macrodeck-plugin verify MyPlugin-1.0.0.signed.macroDeckPlugin --output json
```

That is the whole developer flow: the artifact `build` (or `pack`) produced is what a publishing workflow submits to
the Creator Portal, which signs it - see [Publishing to the Store](/guides/publishing/). There is no
`keygen` or `sign` step on the way to the Store.

## Installing

```bash
dotnet tool install --global MacroDeck.Plugin.Cli --prerelease
```

**`--prerelease` is required today**: only `-preview` versions of the package are published ahead of the
3.0 release, and `dotnet tool install` ignores prerelease versions unless asked. Drop the flag once a
stable 3.0 build ships.

**Requires the ASP.NET Core shared framework**, not just the .NET runtime: the tool pulls
[`MacroDeck.Plugin.Testing`](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/sdk/src/MacroDeck.Plugin.Testing/README.md) transitively, whose
loopback test host is a real Kestrel server. Install the ASP.NET Core runtime (or SDK) alongside the .NET
runtime if `run` or `test` reports it cannot find `Microsoft.AspNetCore.App`.

## Global options

Two options apply to every command, before or after the subcommand name:

| Option | Default | What it does |
| --- | --- | --- |
| `--verbosity <quiet\|normal\|diagnostic>` | `normal` | How much a command narrates while it works. `quiet` suppresses progress narration (`console.Info` lines) but never the command's actual result - a validation report, a conformance report, or a plugin's own console output all still print. |
| `--no-color` | off | Disables ANSI color in text output. |

`--output` is deliberately **not** global: `validate` and `inspect` use it to pick a render format
(`text`/`json`), while `pack` and `test` use it as a file destination path and `new` and `keygen` use it as
a destination directory. Giving it one shared meaning across all of them would have made one of the
meanings surprising.

Running `macrodeck-plugin` with no arguments at all prints the command list and a pointer to
`--help` (exit `UsageError`, 2), rather than the wall of unrecognized-token errors a bare invocation would
otherwise produce. A mistyped command name (`macrodeck-plugin pakc --source .`) gets one line - `'pakc'
is not a macrodeck-plugin command. Did you mean 'pack'?` when a single close command name exists, or a
pointer to `--help` when none is close enough to guess - instead of the typo itself plus one
unrecognized-token error per option that followed it.

## Error and warning output

Every command reports a failure as one line on **stderr**: `error <kebab-case-code>: <sentence>`, red
unless `--no-color` is given. A non-fatal observation - a missing foreign-RID entrypoint, a Debug-looking
source tree, a generated development plugin id - is reported the same way but as `warning <code>:
<sentence>`, also on stderr. A warning **never changes the exit code**; only an `error` line can do that.

Neither shape is gated by `--verbosity quiet`: a diagnostic is the command's result, not narration of what
it is doing, so `quiet` cannot silence it the way it silences `console.Info` progress lines (see the
`--verbosity` row above).

`validate`'s own report is the one exception to the stderr rule: its `error …`/`warning …` lines already
use this same shape, but they are written to **stdout**, because they are the command's result rather than
its error channel - piping `validate --manifest manifest.json > report.txt` has to capture them. Paths
inside any message are printed resolved and absolute, so the same file never reads as two different
strings depending on which command reported it.

This unified shape changes only how a failure or a warning is printed, never what it means for the exit
code - the [exit codes](#exit-codes) below are the contract if you gate CI on a specific code.

## Exit codes

| Code | Meaning |
| --- | --- |
| 0 | Success, or conformant. |
| 1 | The subject is wrong: validation failed, or a required conformance check failed. |
| 2 | Usage error: bad arguments, an unknown `--check`/`--category` token, or an output file that already exists without `--force`. |
| 3 | The input could not be read: a missing file, something that is not a ZIP, or a permissions failure. |
| 4 | Cancelled (Ctrl-C). |
| 70 | An error the command did not anticipate. |

1 and 3 are deliberately distinct - a missing file is an environment problem, not a verdict about the
plugin - which is what makes this tool usable as a CI gate. [`run`](/cli/run/) is the one exception to
this table on a normal exit: it returns the launched plugin's own exit code.

## See also

- [Testing plugins](/sdk/testing/) - `MacroDeck.Plugin.Testing`, which `run` and `test` are built on.
- [Conformance](/sdk/conformance/) - the suite `test` runs, and the report shape all three of its formats
  produce.
- [Plugin hosting](/sdk/hosting/) - the artifact format `pack`/`validate`/`inspect` read, and what the
  supervisor injects that `run` reproduces.
- [Publishing to the Store](/guides/publishing/) - how a plugin is actually published and signed, and why
  `keygen` and `sign` are not part of it.
- [Security model](/policies/security/) - the trust model `verify` checks against, and how the host
  enforces it at install and at every launch.
- [`MacroDeck.Signing` package README](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/sdk/src/MacroDeck.Signing/README.md) -
  the library `sign` and `verify` are built on, shared with the host and the Macro Deck Platform.
- [Certificate schema](/schemas/macrodeck-certificate-v1.schema.json) and
  [package signature schema](/schemas/macrodeck-package-signature-v1.schema.json) - the exact shape of
  `certificate.json` and the embedded `signature`/`files` fragments.
- [`MacroDeck.Plugin.Cli` package README](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/sdk/src/MacroDeck.Plugin.Cli/README.md).
