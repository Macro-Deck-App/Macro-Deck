---
title: macrodeck-plugin run
description: Launch a plugin exactly as the supervisor composes its environment, against the running host or a disposable stub.
---

Launches a plugin exactly as the supervisor composes its environment, against the running Macro Deck
host by default, streaming its output live. This is the fastest way to reproduce what the supervisor
injects - see [plugin-hosting.md: what the supervisor injects](/reference/plugin-hosting/#what-the-supervisor-injects)
for the production side of the same table.

`run` starts the plugin as a child process. For breakpoints, child-process attach and the simpler
direct-project launch against a desktop host, see [Debugging plugins](/guides/debugging/).

| Option | Default | What it does |
| --- | --- | --- |
| `--project <path>` | - | A plugin's `.csproj`, or its directory. |
| `--executable <path>` | - | An already-built executable or framework-dependent `.dll`. |
| `--artifact <path>` | - | A packed `.macroDeckPlugin` artifact. |
| `--host-url <url>` | the running host | A real host's URL, when the discovered one is not the one you want. |
| `--stub-host` | - | Run against a disposable in-process stub host instead of a real one. |
| `--mode <managed\|self-registering>` | `self-registering` | The registration mode to launch under. Managed needs `--stub-host`. |
| `--pairing <bool>` | `true` | Self-registering only. Whether the CLI relies on interactive pairing when running against a real host, reporting what pairing will do instead of demanding a token up front. Set `false` to require `--enrollment-token` instead. |
| `--enrollment-token <token>` | - | Self-registering only. Optional against a real host while pairing is enabled (the default); required when `--pairing false` is given and fails with `enrollment-token-required` if omitted. A placeholder is generated against the stub host. A real value appears in the command line, so prefer letting pairing supply the credential, or the direct-project enrollment described below. |
| `--plugin-id <id>` | - | Managed only. Defaults to the id declared by the `manifest.json` next to the launch target; a generated development id against the stub host only when no manifest id is found. |
| `--secret <secret>` | - | Managed only. A generated secret against the stub host if omitted. |
| `--state-directory <dir>` | temp dir | Self-registering only. Where the persisted credential is stored. |
| `--data-directory <dir>` | temp dir | Managed only. |
| `--instance-id <id>` | fresh id | Both modes. |
| `--launch-id <id>` | fresh id | Managed only, diagnostic - never asserted on the wire. |
| `--listen-url <url>` | `http://127.0.0.1:0` | Where the plugin listens. |

Exactly one of `--project`/`--executable`/`--artifact` is required. `--host-url` and `--stub-host` are
mutually exclusive.

## Which host `run` uses

**The running Macro Deck host is the default.** While the host runs it publishes the loopback port it
actually bound to a file in the system temp directory - `macro-deck-host.port`, or
`macro-deck-host-development.port` for a Development build - and deletes that file when it stops. `run`
reads it and points the plugin at `http://127.0.0.1:<port>`, the very endpoint the supervisor injects as
`MACRO_DECK_PLUGIN_HOST_URL` for a managed plugin. Both names are probed, because this CLI ships
independently of any host build; if a Development and a Production host are running side by side, the
port file written most recently wins - the host you started last. Pass `--host-url` to override the
choice, and `--stub-host` to skip discovery entirely.

If no port file is readable, `run` fails with `host-not-found` (exit code 3) rather than quietly falling
back to the stub: a plugin talking to a stub behaves differently enough from one talking to Macro Deck
that a silent substitution would be its own debugging session.

**`--mode self-registering` is the default, and it is the only mode a real host accepts.** A managed
launch needs a launch bootstrap token only a real supervisor can mint, which `run` cannot manufacture
against a host it did not start itself, so **`--mode managed` requires `--stub-host`**. Against a real
host and by default (`--pairing true`), `run` lets the approval prompt that appears in Macro Deck supply
the credential, rather than requiring `--enrollment-token` up front. Before launching, it asks the host
whether pairing is actually available and reports one of:

| Diagnostic | When |
| --- | --- |
| `developer-mode-disabled` | The host says **Developer Mode** is off, so no prompt can appear. `run` keeps going: the plugin retries and pairs as soon as you turn it on, with nothing restarted. |
| `pairing-prompt-expected` | The host could not be asked - unreachable, or too old to report it. A prompt is expected but not guaranteed. |
| *(neither)* | The host confirmed Developer Mode is on; a prompt will appear. |

Once a request is actually pending, the plugin writes `Waiting for pairing approval in Macro Deck...` to
stderr, which `run` forwards, and reports the outcome the same way once it is approved or rejected. Passing `--enrollment-token` still works and exposes the plaintext through the command line, so
prefer letting pairing supply the credential, or use the masked one-time enrollment and direct IDE
profile in [Debugging plugins](/guides/debugging/) for interactive real-host development; use
`--stub-host` for an isolated CLI loop that needs no Macro Deck installation at all.

Against the stub host, `run` starts a real, in-process `MacroDeckTestHost` - the actual registration,
session and WebSocket implementation, not a mock - and points the launched process at it. Every
`MACRO_DECK_PLUGIN_*` variable and `ASPNETCORE_URLS` are scrubbed from the inherited environment first,
then set fresh for the resolved mode, exactly mirroring what a managed plugin's supervisor sets - see the
table in the plugin hosting guide's ["What the supervisor injects"](/reference/plugin-hosting/#what-the-supervisor-injects) section.

**In managed mode (`--stub-host` only), `MACRO_DECK_PLUGIN_ID` is read from the manifest, not invented.**
A real supervisor always launches a plugin whose own `manifest.json` declares the same id it injects, and
the host rejects any disagreement between the two - so `run` reads `id` from the `manifest.json` next to
the resolved launch target and injects that. `--plugin-id` still overrides it explicitly; a generated
development id is used only as a last resort, when neither `--plugin-id` nor a manifest id is found, and
that case prints `warning plugin-id-generated: …` naming the way out. This is also what makes
`run --project` work at all - without it, a plugin whose manifest declares a real id could never agree
with a generated one.

At `--verbosity quiet`, the plugin's own `[plugin]`/`[plugin:stderr]` output still always prints - it is
the command's actual result, not narration - but the forwarded stub-host log lines (`[log:…]`) are
suppressed along with the rest of `run`'s progress narration. Each of the child's streams is forwarded
to the matching one of `run` itself: `[plugin]` on stdout, `[plugin:stderr]` on stderr.

**Ctrl-C runs the documented shutdown sequence** against the stub host: `session.goodbye`, then a close
with code `4004` (`SupervisorShutdown`), then the manifest's own graceful timeout (clamped 1-60s, 10s if
none is found), then a process-tree kill if the plugin has not exited by then. Against a real host there
is no session this process owns to say goodbye on, so only the grace-then-kill half applies.

**On a normal exit, `run` returns the launched plugin's own exit code**, not one of the fixed values in
the [exit codes](/cli/#exit-codes) table above - `run` is a launcher, and the plugin's exit code is the only
honest signal for whether it exited cleanly. `Cancelled` (4) is always returned after Ctrl-C, whether the
process exited gracefully or had to be killed; the fixed usage/input codes still apply to a `run` that
never got as far as launching a process at all.
