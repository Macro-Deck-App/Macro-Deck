---
title: macrodeck-plugin run
description: Launch a plugin with the environment the supervisor would give it, against the running host or a disposable stub.
---

Launches a plugin as a child process with the environment the supervisor would give it, and streams its
output live.

## Examples

```bash
macrodeck-plugin run --project src/HelloDeck --stub-host
```

```text
Started a disposable stub host at http://127.0.0.1:49789.
Started process 18593 (mode: SelfRegistering, host: http://127.0.0.1:49789). Press Ctrl-C to stop.
[plugin] info: Microsoft.Hosting.Lifetime[14]
[plugin]       Now listening on: "http://127.0.0.1:49790"
...
[plugin] info: MacroDeck.Plugin.Hosting.Transport.PluginConnectionHostedService[0]
[plugin]       Registered with the host as '"com.example.hello-deck"'.
Session established (negotiated plugin protocol v3).
[plugin] info: HelloDeck.PluginIntegration[0]
[plugin]       Initialized.
[log:information] HelloDeck.PluginIntegration: Initialized.
...
^CStopping the plugin...
The plugin did not exit within its 10s grace period; killing it.
```

An isolated loop with no Macro Deck installed. The stub is a real in-process `MacroDeckTestHost`, not a
mock.

```bash
macrodeck-plugin run --project src/HelloDeck
```

Against the running Macro Deck. Approve the pairing prompt that appears in Macro Deck - see
[Pairing with a real host](#pairing-with-a-real-host).

```bash
macrodeck-plugin run --artifact ./artifacts/com.example.hello-deck-1.0.0-osx-arm64.macroDeckPlugin --stub-host
```

Run exactly what you packed, rather than the Debug build.

```bash
macrodeck-plugin run --project src/HelloDeck --stub-host --mode managed
```

Reproduce a managed (supervisor-launched) plugin. Managed mode works only against the stub.

```bash
macrodeck-plugin run --executable ./bin/Debug/net10.0/HelloDeck.dll --host-url http://127.0.0.1:5000
```

An already-built executable or framework-dependent `.dll`, against a host you name explicitly.

For breakpoints, child-process attach and the direct-project launch against a desktop host, see
[Debugging plugins](/guides/debugging/).

## Options

Exactly one of `--project`, `--executable` or `--artifact` is required.

| Option | Default | Description |
| --- | --- | --- |
| `--project <path>` | - | A plugin's `.csproj`, or its directory. |
| `--executable <path>` | - | An already-built executable or framework-dependent `.dll`. |
| `--artifact <path>` | - | A packed `.macroDeckPlugin` artifact. |
| `--host-url <url>` | the running host | A real host's URL; cannot be combined with `--stub-host`. |
| `--stub-host` | off | Run against a disposable in-process stub host. |
| `--mode <managed\|self-registering>` | `self-registering` | Registration mode; `managed` needs `--stub-host`. |
| `--pairing <true\|false>` | `true` | Self-registering only: let interactive pairing supply the credential when no token is given. |
| `--enrollment-token <token>` | placeholder on the stub | Self-registering only: required against a real host when `--pairing false`. |
| `--plugin-id <id>` | the manifest's `id` | Managed only: the injected plugin id. |
| `--secret <secret>` | generated on the stub | Managed only. |
| `--state-directory <dir>` | temp dir | Self-registering only: where the persisted credential is stored. |
| `--data-directory <dir>` | temp dir | Managed only. |
| `--instance-id <id>` | fresh id | Both modes. |
| `--launch-id <id>` | fresh id | Managed only, diagnostic; never asserted on the wire. |
| `--listen-url <url>` | `http://127.0.0.1:0` | Where the plugin listens; port 0 lets the OS pick. |

Temp directories `run` created itself are deleted when it exits.

## Which host `run` uses

```bash
macrodeck-plugin run --project src/HelloDeck --host-url http://127.0.0.1:5000   # this host
macrodeck-plugin run --project src/HelloDeck --stub-host                        # no discovery at all
```

- **The running Macro Deck host is the default.** While it runs, the host writes the loopback port it
  bound to `macro-deck-host.port` (or `macro-deck-host-development.port` for a Development build) in the
  system temp directory, and deletes it on exit. `run` reads it and points the plugin at
  `http://127.0.0.1:<port>` - the same URL the supervisor injects as `MACRO_DECK_PLUGIN_HOST_URL`.
- Both names are probed. If a Development and a Production host run side by side, the most recently
  written port file wins - the host you started last.
- No readable port file fails with `host-not-found` (exit 3). `run` never falls back to the stub
  silently.

## Pairing with a real host

**`self-registering` is the default, and the only mode a real host accepts.** A managed launch needs a
launch bootstrap token that only a real supervisor can mint, so `--mode managed` without `--stub-host`
fails with `managed-needs-stub-host` (exit 2).

With pairing on (the default) and no token, `run` asks the host whether pairing is available and reports:

| Diagnostic | Meaning |
| --- | --- |
| `developer-mode-disabled` | Developer Mode is off, so no prompt can appear. `run` keeps going; the plugin pairs as soon as you turn it on, with no restart. |
| `pairing-prompt-expected` | The host could not be asked (unreachable or too old). A prompt is expected but not guaranteed. |
| *(neither)* | Developer Mode is on; a prompt will appear. |

Once a request is pending, the plugin writes `Waiting for pairing approval in Macro Deck...` to stderr,
and reports the outcome the same way after you approve or reject it.

### Using an enrollment token instead

```bash
macrodeck-plugin run --project src/HelloDeck --pairing false --enrollment-token <token>
```

```text
$ macrodeck-plugin run --project src/HelloDeck --pairing false
error enrollment-token-required: --mode self-registering against a real host needs --enrollment-token when --pairing is off.
```

:::caution
A token passed this way is visible in plaintext on the command line. Prefer pairing, or the masked
one-time enrollment and IDE profile in
[Debugging plugins](/guides/debugging/#advanced-enroll-with-a-developer-token-for-headless-runs).
:::

## The environment `run` composes

Every inherited `MACRO_DECK_PLUGIN_*` variable and `ASPNETCORE_URLS` is scrubbed, then set fresh for the
resolved mode, mirroring
[what the supervisor injects](/reference/plugin-hosting/#what-the-supervisor-injects).

In managed mode, `MACRO_DECK_PLUGIN_ID` comes from the `manifest.json` next to the launch target, because
the host rejects an injected id that disagrees with the manifest. `--plugin-id` overrides it. Only when
neither exists does `run` generate a development id, and it warns:

```text
warning plugin-id-generated: No manifest.json next to the launch target, so a development id was generated. ...
```

## Output

- `[plugin]` lines are the plugin's stdout and go to `run`'s stdout; `[plugin:stderr]` lines go to
  stderr.
- `[log:...]` lines are the stub host's forwarded logs.
- `--verbosity quiet` hides `[log:...]` lines and progress narration. The plugin's own output always
  prints.

## Stopping

Ctrl-C runs the supervisor's shutdown sequence against the stub host:

1. `session.goodbye`.
2. Close with code `4004` (`SupervisorShutdown`).
3. Wait for the manifest's `shutdown.gracefulTimeoutSeconds` (clamped to 1-60s; 10s if absent).
4. Kill the process tree if it is still running.

Against a real host, `run` owns no session to say goodbye on, so only steps 3 and 4 apply.

## Exit codes

| Code | When |
| --- | --- |
| the plugin's own | The plugin exited on its own. |
| 2 | Usage error: wrong number of launch targets, `--host-url` with `--stub-host`, `managed-needs-stub-host`, `enrollment-token-required`. |
| 3 | `host-not-found`, a project that fails to build, an unreadable artifact, or a process that fails to start. |
| 4 | Ctrl-C, whether the plugin exited gracefully or had to be killed. |

See the [shared exit codes](/cli/#exit-codes).

## See also

- [Debugging plugins](/guides/debugging/) - breakpoints, IDE profiles, enrollment tokens.
- [Plugin hosting](/reference/plugin-hosting/) - registration modes and every `MACRO_DECK_PLUGIN_*`
  variable.
- [`macrodeck-plugin test`](/cli/test/) - the conformance suite, against the same stub host.
