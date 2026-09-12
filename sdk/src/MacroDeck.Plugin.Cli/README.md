# MacroDeck.Plugin.Cli

`macrodeck-plugin`, the developer CLI for [Macro Deck](https://github.com/Macro-Deck-App/Macro-Deck)
plugins: validate a manifest, inspect or pack a `.macroDeckPlugin` artifact, run a plugin against the
running host (or a disposable stub) with production-shaped shutdown, and run the plugin conformance
suite - all without installing Macro Deck itself.

## Install

```bash
dotnet tool install --global MacroDeck.Plugin.Cli --prerelease
```

**`--prerelease` is required today**: only `-preview` versions are published ahead of the 3.0 release.
Drop the flag once a stable 3.0 build ships.

**Requires the ASP.NET Core shared framework** (not just the .NET runtime): this tool pulls
[`MacroDeck.Plugin.Testing`](https://www.nuget.org/packages/MacroDeck.Plugin.Testing) transitively, whose
loopback test host is a real Kestrel server. Install the ASP.NET Core runtime (or SDK) alongside the .NET
runtime if `macrodeck-plugin run` or `macrodeck-plugin test` reports it cannot find
`Microsoft.AspNetCore.App`.

## Commands

| Command | What it does |
| --- | --- |
| `build` | Builds every runtime identifier `manifest.entrypoints` declares using the per-target recipes in `macrodeck-build.json`, stages them under their declared `runtimes/<rid>/` directories so no two platforms collide, then packages the result with the same implementation `pack` uses. Fails when a requested runtime identifier does not produce the entrypoint its manifest declares, and never signs. |
| `validate` | Validates a manifest, a version directory, or an artifact: the real manifest reader, the embedded JSON Schema, the permission vocabulary, and declared file digests checked against real bytes. Reports every independent problem in one run. |
| `inspect` | Reports what installing an artifact or version directory would find - entrypoints (each flagged if missing from the payload), permissions, dependencies, conflicts, icon packs, compatibility, signature shape, size and compression ratio - without a running host. |
| `pack` | Builds a `.macroDeckPlugin` artifact from a source tree, validating the manifest first and recomputing `files[]` digests, so a bad manifest or a rejected entry never becomes an artifact. Warns (without failing) about an entrypoint the manifest declares but the artifact does not contain. |
| `run` | Launches a plugin exactly as the supervisor composes its environment, against the running desktop host by default - discovered through the loopback port file the host publishes - or a disposable stub host via `--stub-host`, streaming its output live. In managed mode, the injected plugin id comes from the launch target's own `manifest.json` rather than being invented, matching what a real supervisor does. Ctrl-C runs the documented shutdown sequence against the stub host. |
| `test` | Runs the plugin conformance suite (`MacroDeck.Plugin.Testing.Conformance`) against a project, executable or artifact, and writes a text, JSON or Markdown report. |
| `keygen` | Generates a creator Ed25519 key pair for `sign`, and never issues a certificate - only the Creator Portal can turn a public key into a certificate the Macro Deck root has signed. Not part of publishing to the Store; see below. |
| `sign` | Signs a `.macroDeckPlugin`, `.macroDeckIconPack`, `.macroDeckProfile`, `.macroDeckFolder` or `.macroDeckWidget` package with a creator certificate and private key: chain-verifies the certificate against the pinned Macro Deck root first, then embeds the signature in the artifact's own manifest and writes `certificate.json`/`certificate.sig` to the archive root - there is no detached signature file. Re-verifies the written artifact before reporting success. Not part of publishing to the Store; see below. |
| `verify` | Verifies a signed package's embedded signature and certificate against the pinned Macro Deck root. Cryptographic verification only - revocation is never checked, and every run says so. |

Run `macrodeck-plugin <command> --help` for every command's options.

## Exit codes

| Code | Meaning |
| --- | --- |
| 0 | Success, or conformant. |
| 1 | The subject is wrong: validation failed, or a required conformance check failed. |
| 2 | Usage error: bad arguments, or an unknown `--check` id. |
| 3 | The input could not be read: a missing file, something that is not a ZIP, or a permissions failure. |
| 4 | Cancelled (Ctrl-C). |
| 70 | An error the command did not anticipate. |

1 and 3 are deliberately distinct - a missing file is an environment problem, not a verdict about the
plugin - which is what makes this tool usable as a CI gate.

Every command reports a failure as one line, `error <kebab-case-code>: <message>`, and a non-fatal
observation the same way as `warning <code>: <message>` - neither suppressed by `--verbosity quiet`. Both
go to stderr, except `validate`'s own report, which is the command's result rather than its error channel
and stays on stdout. This unified shape is new, but it changes only how a result is printed: every code above still
means exactly what it did before. See [the CLI documentation](https://docs.macro-deck.app/cli/#error-and-warning-output)
for the full shape.

## Examples

```bash
# Build every platform the manifest declares and package the result.
macrodeck-plugin build --output ./artifacts

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

# Run the conformance suite against the packed artifact and write a Markdown report.
macrodeck-plugin test --artifact MyPlugin-1.0.0.macroDeckPlugin --report markdown --output conformance.md

# Verify a signed artifact, e.g. as a CI gate.
macrodeck-plugin verify MyPlugin-1.0.0.signed.macroDeckPlugin
```

## Signing and the Store

Artifacts published to the Macro Deck Store are signed by the Creator Portal, server-side, after it
verifies the publishing workflow's identity and provenance. Plugin developers and their CI workflows never
generate, receive or hold signing keys or certificates, and there is no manual upload step. `keygen` and
`sign` are for artifacts distributed outside the Store and for Macro Deck's own infrastructure - they are
not a step on the way to publishing. See
[Publishing to the Store](https://docs.macro-deck.app/guides/publishing/).

See [the plugin development documentation](https://docs.macro-deck.app/introduction/quickstart/).

Licensed under Apache-2.0.
