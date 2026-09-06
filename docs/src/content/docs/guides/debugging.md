---
title: Debugging plugins
description: Run a .NET plugin under an IDE, choose between the disposable stub host and a real Macro Deck host, keep enrollment credentials out of source control, and diagnose connection failures.
---

There are two useful debugging loops for an out-of-process .NET plugin. Use the CLI's disposable stub
host when the plugin protocol and process lifecycle are the subject. Start the plugin project directly
from the IDE when you need the real Macro Deck UI and immediate breakpoints in your own code.

This page is about a **plugin** built with `MacroDeck.Plugin.Hosting`. An in-process integration runs
inside Macro Deck itself and has a different debugging model.

Run every command on this page from the plugin project directory: the directory that contains its
`.csproj` and `manifest.json`. If your terminal is still at the solution root created by the
[Quickstart](/introduction/quickstart/), move into the project first:

```bash
cd src/Acme.LightControl
```

## Choose the host first

| Debugging loop | What actually runs | Best for | Debugger consequence |
| --- | --- | --- | --- |
| `macrodeck-plugin run --project . --stub-host` | The CLI starts a disposable `MacroDeckTestHost`, builds the project, then launches the plugin as a child process. `--stub-host` is what selects the stub: without it, `run` connects to the running desktop host in self-registering mode. | Registration, session negotiation, reconnect, shutdown and a clean reproduction of the supervisor's launch environment, without installing Macro Deck. | The CLI is the parent process. Attach to its .NET child, not just to `macrodeck-plugin`. |
| The IDE starts the plugin project against `http://127.0.0.1:8193` | The project is the debuggee and connects to the already-running desktop host in self-registering mode. | Actions triggered from the real UI, host callbacks, forwarded logs and end-to-end behaviour. | Rider and Visual Studio bind breakpoints directly because they launched the plugin process. |

Start with the stub host when a real UI is not part of the problem:

```bash
macrodeck-plugin run --project . --stub-host
```

This is not a mocked protocol. `run` uses the real registration, session and WebSocket implementation
from `MacroDeck.Plugin.Testing`, composes the same environment as the supervisor, and streams the
child's output. It also prints the child process id, which is useful when attaching manually.

Use the direct project profile below for the shortest breakpoint-to-action loop against a real host.
The host must already be running on the same machine; plugin protocol endpoints reject non-loopback
callers.

## Reusable IDE profiles

Add `Properties/launchSettings.json` to the plugin project. Keep the capital `S` in
`launchSettings.json`; the exact path also lets the Macro Deck analyzers inspect the shared profile on
case-sensitive systems.

```json
{
  "$schema": "https://json.schemastore.org/launchsettings.json",
  "profiles": {
    "Macro Deck - Real Host": {
      "commandName": "Project",
      "dotnetRunMessages": true,
      "launchBrowser": false,
      "workingDirectory": "$(ProjectDir)",
      "environmentVariables": {
        "ASPNETCORE_ENVIRONMENT": "Development",
        "MACRO_DECK_PLUGIN_MODE": "SelfRegistering",
        "MACRO_DECK_PLUGIN_HOST_URL": "http://127.0.0.1:8193",
        "MACRO_DECK_PLUGIN_STATE_DIRECTORY": ".macrodeck-dev-state"
      }
    },
    "Macro Deck - Stub Host (CLI)": {
      "commandName": "Executable",
      "executablePath": "macrodeck-plugin",
      "workingDirectory": "$(ProjectDir)",
      "commandLineArgs": "run --project . --stub-host"
    }
  }
}
```

Neither profile contains a credential. The real-host profile's settings mean:

- `commandName: Project` makes the plugin itself the debuggee. There is no CLI parent process.
- `ASPNETCORE_ENVIRONMENT: Development` enables the normal ASP.NET Core Development configuration
  for the debug run. It does not supply a credential.
- `SelfRegistering` is the only mode an independently started plugin can use against a real host. A
  managed launch needs a bootstrap token that only the real supervisor can mint.
- `MACRO_DECK_PLUGIN_HOST_URL` is the **Macro Deck host** address, not the address on which the plugin
  serves its own health endpoints.
- `.macrodeck-dev-state` is resolved from the project working directory selected by the profile.

The stub-host profile is the launch-profile equivalent of `macrodeck-plugin run --project . --stub-host`.
The
global tool must be on the IDE's `PATH`; if the IDE cannot resolve it, set `executablePath` to the
absolute path of the installed tool on that machine and keep that machine-specific change private.

Do **not** add `ASPNETCORE_URLS`, `applicationUrl`, `UseUrls(...)`, or a `urls` configuration value.
Without an override, the SDK binds the plugin to `http://127.0.0.1:0`, an operating-system-selected
loopback port. In a managed installation the supervisor supplies the exact listener through
`ASPNETCORE_URLS` and probes that address. Overriding it makes the plugin listen somewhere the
supervisor does not expect; [MDP4002](/sdk/analyzers/#mdp4002) catches the statically visible
forms of this mistake.

## Press F5 and approve the prompt

A self-registering plugin needs no credential you create, copy or manage. Press F5 in your IDE (or run
`dotnet run` with the direct profile above), and when the desktop app shows a pairing approval prompt,
approve it there. The plugin's SDK creates the pairing request, waits for approval, redeems it, and
persists the resulting per-plugin secret before it is ever used.

This needs **Developer Mode** turned on in the desktop app's settings - it is off by default. Without
it, no pairing prompt appears; see [Common failures](#common-failures) below.

Before the first run, add the state directory to the repository's `.gitignore`:

```text
.macrodeck-dev-state/
```

The directory will contain the long-lived per-plugin secret and fallback logs. Ignoring it before the
first run prevents an ordinary `git add .` from staging credentials.

On the first successful connection, the SDK writes the secret before using it:

```text
.macrodeck-dev-state/
└── <plugin-id>/
    └── credentials.json
```

The credential file also records the host that issued the secret. It is owner-only on Unix. On
Windows it inherits the state directory's ACL. It is **not encrypted at rest** on either platform.

After `/_macrodeck/ready` returns 200, or the logs report that the plugin registered and connected, the
credential is in place. Later runs load `credentials.json` directly and need no further approval.

If the state directory is lost while the host still remembers the plugin, the next pairing prompt offers
to **replace the development credential** instead of dead-ending - see
[Registration is refused as already registered](#registration-is-refused-as-already-registered) below.

## Advanced: enroll with a Developer token for headless runs

CI, build scripts, and other unattended setups have no human available to approve a pairing prompt. For
those, use a reusable Developer token instead of pairing.

A Developer token replaces the approval prompt, not **Developer Mode**. The host refuses both enrolment
and session creation for a development credential while Developer Mode is off, so a machine that runs
plugins unattended needs the setting enabled once; it then persists, and toggling it takes effect
immediately without restarting Macro Deck.

A self-registering plugin needs a Developer token only when its state directory contains no stored
credential. Create one under **Developer Tools → Plugin development → Credentials** in the desktop app.
The plaintext is shown once, so treat it as a secret. A token you have revoked stays in that list until
you remove it, which also deletes the plugin registrations that enrolled with it.

Do not put the token in `launchSettings.json`, User Secrets, CLI arguments or another persisted
configuration. Run the project once from a terminal with the token held only in an environment
variable. For zsh:

```bash
read -s "MACRO_DECK_PLUGIN_ENROLLMENT_TOKEN?Enrollment token: "
export MACRO_DECK_PLUGIN_ENROLLMENT_TOKEN
echo
MACRO_DECK_PLUGIN_MODE=SelfRegistering \
  MACRO_DECK_PLUGIN_HOST_URL=http://127.0.0.1:8193 \
  MACRO_DECK_PLUGIN_STATE_DIRECTORY=.macrodeck-dev-state \
  dotnet run
unset MACRO_DECK_PLUGIN_ENROLLMENT_TOKEN
```

For Bash, replace the first line with
`read -rsp "Enrollment token: " MACRO_DECK_PLUGIN_ENROLLMENT_TOKEN`. In PowerShell 7:

```powershell
$env:MACRO_DECK_PLUGIN_ENROLLMENT_TOKEN = Read-Host "Enrollment token" -MaskInput
$env:MACRO_DECK_PLUGIN_MODE = "SelfRegistering"
$env:MACRO_DECK_PLUGIN_HOST_URL = "http://127.0.0.1:8193"
$env:MACRO_DECK_PLUGIN_STATE_DIRECTORY = ".macrodeck-dev-state"
dotnet run
Remove-Item Env:MACRO_DECK_PLUGIN_ENROLLMENT_TOKEN
Remove-Item Env:MACRO_DECK_PLUGIN_MODE
Remove-Item Env:MACRO_DECK_PLUGIN_HOST_URL
Remove-Item Env:MACRO_DECK_PLUGIN_STATE_DIRECTORY
```

On the first successful connection, the SDK exchanges the Developer token for a per-plugin secret
and writes it to the same `.macrodeck-dev-state/<plugin-id>/credentials.json` path pairing uses, with the
same owner-only handling and no encryption at rest.

After `/_macrodeck/ready` returns 200, or the logs report that the plugin registered and connected,
stop the process. The commands above remove the token from the shell; later IDE starts load
`credentials.json` and do not need it.

Discard the plaintext after enrollment, but keep the Developer token itself active in Macro Deck for
as long as you use the self-registered plugin. Revocation cascades to every registration it created,
and expiry also prevents those registrations from opening new sessions. The stored per-plugin secret
has no independent expiry, but the host still checks that its owning Developer token is active and
unexpired. A dedicated token limits the effect to your development registrations when you eventually
remove them.

Do not delete `credentials.json` just to remove the enrollment token: the file contains the separate
per-plugin secret, while the host still owns the matching registration. Avoid putting a real token in CLI
arguments: shell history, process inspection and screenshots can expose it.

## Breakpoints in Rider and Visual Studio

With the direct profile, choose **Macro Deck - Real Host** and start the IDE's Debug action. A
breakpoint in `Program.cs` is hit during process startup. A breakpoint in
`IPluginIntegration.InitializeAsync` is hit only after a session has opened, because initialization is
connection-gated rather than process-start-gated. A breakpoint in an action or capability handler is
hit only after the corresponding operation is triggered from Macro Deck.

If `/_macrodeck/health` is available but `InitializeAsync` has not run, inspect
`/_macrodeck/ready` and `/_macrodeck/diagnostics` before treating the breakpoint as broken: the process
can be live while it is still reconnecting.

### When the CLI launches the plugin

`macrodeck-plugin run` launches a second .NET process. Debugging only the CLI will not bind breakpoints
in the plugin.

- In Rider, create a .NET Executable configuration for `macrodeck-plugin`, use
  `run --project . --stub-host` as the arguments, set the working directory to the plugin project, and enable
  **Attach to child .NET processes**. The **Macro Deck - Stub Host (CLI)** launch profile already
  supplies the executable, arguments and working directory.
- In Visual Studio, or when the CLI was started in a terminal, wait for the CLI's
  `Started process <pid>` line, then use **Debug → Attach to Process** and select that PID. A
  framework-dependent plugin may appear as `dotnet`; distinguish it by PID or by the command line
  ending in the plugin DLL.
- In any other .NET debugger, attach to the same child PID with the CoreCLR/.NET debugger.

`run --project` performs a normal Debug build unless the project changes its default configuration,
so the matching PDB is normally already present. If a breakpoint remains hollow, verify that the
debugger is attached to the child PID printed by the current run, rather than a previous plugin or the
CLI parent.

## Logs

The three useful log surfaces are different:

- `macrodeck-plugin run` always streams the child process's stdout and stderr, prefixed `[plugin]` and
  `[plugin:stderr]` and written to the CLI's own stdout and stderr respectively. Against the stub host
  it also narrates forwarded host-side log events at normal verbosity.
- The direct IDE profile shows process startup, unhandled failures and the plugin's own log output in
  the Debug/Run console.
- A plugin that calls `.UseMacroDeckLogging()` forwards its Serilog events to the real host's log
  viewer once connected, *and* keeps writing them to its own console through the logging providers the
  host builder already registered - so the same events are visible in the IDE console and, under
  `macrodeck-plugin run`, as `[plugin]` lines. Adding a Serilog console sink of your own on top prints
  every line twice. The default forwarded minimum is `Information`; this is independent of any lower
  minimum used by a local Serilog sink.

When `.UseMacroDeckLogging()` cannot deliver a batch, its default bounded diagnostic tail is:

```text
.macrodeck-dev-state/<plugin-id>/logs/plugin-fallback.log
```

The fallback file is not replayed into the host after reconnecting, and `log.publish` has no
acknowledgement. If an event is missing from the host viewer, check the fallback file, connection
readiness, the configured log level and whether `.UseMacroDeckLogging()` is actually called. See
[Logging and health](/sdk/logging/) for queue, rate and size limits.

## Inspect the plugin's own endpoints

The following routes are served by the **plugin process**, not by Macro Deck at port 8193:

| Route | How to read it while debugging |
| --- | --- |
| `GET /_macrodeck/health` | HTTP 200 as soon as the plugin web application serves. This proves liveness only. |
| `GET /_macrodeck/ready` | HTTP 200 after a session opens; HTTP 503 before that. |
| `GET /_macrodeck/info` | Manifest identity, resolved registration mode, SDK version and negotiated protocol version. |
| `GET /_macrodeck/diagnostics` | Connection status, `faultReason`, session id, reconnect attempt, last close code, invocation count and declared/accepted capability counts. |

Find the actual loopback URL in the plugin's `Now listening on: http://127.0.0.1:<port>` lifetime log,
then query it, for example:

```bash
curl -i http://127.0.0.1:<plugin-port>/_macrodeck/health
curl -i http://127.0.0.1:<plugin-port>/_macrodeck/ready
curl -s http://127.0.0.1:<plugin-port>/_macrodeck/info
curl -s http://127.0.0.1:<plugin-port>/_macrodeck/diagnostics
```

Do not substitute `8193` for `<plugin-port>`: `8193` is the host URL the plugin connects **to**. The
plugin's listener is a separate, normally ephemeral loopback address.

## Common failures

### The process starts, but readiness remains 503

`health` with 200 and `ready` with 503 means the process is serving but no session is open. Read
`diagnostics.status` and `diagnostics.faultReason`, then check that the real host is running, the host
URL is loopback, and the stored credential belongs to this host. A temporarily unreachable host is not
a startup error by default: the SDK keeps retrying with backoff.

### No pairing prompt appears

Check, in order: **Developer Mode** is turned on in the desktop app's settings (it is off by default,
and pairing is unreachable while it is off); the plugin's `MACRO_DECK_PLUGIN_HOST_URL` actually points at
the desktop app you are looking at, not a different host instance; and the desktop app is the one
running - pairing prompts appear only in it, never headlessly.

Turning Developer Mode on is enough on its own - neither the plugin nor Macro Deck needs restarting. A
plugin that found it off creates no request, says so on stderr, and pairs on its next reconnect tick.
Once a request is pending the desktop app raises a system notification and leaves an entry in the
notification list, so a prompt behind another window is still findable. See
[Troubleshooting](/guides/troubleshooting/#pairing) for the full symptom list, including rejection,
expiry and `429` on create.

### The plugin says it has no stored credential and no enrollment token

This applies to the advanced, headless enrollment path only - the default pairing path does not use an
enrollment token. During the one-time enrollment run, check all four pieces together:

1. The token was exported in the same shell that starts `dotnet run`.
2. The exact variable name is `MACRO_DECK_PLUGIN_ENROLLMENT_TOKEN`.
3. `MACRO_DECK_PLUGIN_MODE` is `SelfRegistering` and the host URL is the loopback desktop host.
4. The process working directory is the plugin project, so `.macrodeck-dev-state` points where expected.

### Registration is refused as already registered

The host already has a registration for the manifest's plugin id, but this working directory no longer
has the matching `credentials.json` - often because the state directory was deleted, or this is a fresh
clone of the plugin's repository. Run the plugin again with Developer Mode on: the pairing approval
prompt detects the existing registration and offers to **replace the development credential**. Approving
that redeems a new secret, rotates it in on the host, and terminates the plugin's old sessions, so there
is no window where both secrets work. This is the supported recovery - it needs no manual registration
revocation and no restoring a backed-up state directory.

### A second debug instance replaces the session

Only one live session per plugin id is allowed. Close the installed/managed instance or the other IDE
run before starting this one. A WebSocket close with code `4000` (`SESSION_REPLACED`) is the expected
symptom.

### The host reports an installed plugin as unhealthy

Remove every listener override: `ASPNETCORE_URLS`, `applicationUrl`, `UseUrls(...)`, and `urls` in
configuration. The supervisor chooses and probes the listener address. If the listener is untouched,
verify the manifest's `health.path` (default `/_macrodeck/health`) and its timeout settings.

### The manifest cannot be found

The SDK reads `manifest.json` from the content root. Keep it at the plugin project root for the direct
`commandName: Project` profile and copy it to build output for CLI or executable launches:

```xml
<ItemGroup>
    <Content Include="manifest.json" CopyToOutputDirectory="PreserveNewest" />
    <Content Include="Assets\icon.svg" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

If an IDE overrides the direct profile's working directory, restore it to the project directory rather
than adding an absolute machine-specific content root to the shared profile.

### Logs appear locally but not in the host viewer

Confirm that `.UseMacroDeckLogging()` is called, `/_macrodeck/ready` is 200, and the event meets
`MacroDeck:Plugin:Logging:MinimumLevel`. Forwarding is fire-and-forget and has no replay, so inspect
`plugin-fallback.log` for the most recent undelivered tail.

## If development credentials leak

The shared `launchSettings.json` above contains no secret and can be committed. The state directory
must not be committed because it contains the long-lived per-plugin secret and fallback logs. Keep the
`.gitignore` rule added before the first run in the repository.

If `credentials.json` was ever committed or shared, removing it from the next commit is not enough: run
the plugin again with Developer Mode on, delete the local `credentials.json` first if it is still
present, and approve the resulting pairing prompt's **replace the development credential** option. That
rotates the plugin's secret host-side and keeps the replacement state private; there is no need to
revoke a registration by hand.

If you used the advanced Developer-token path and the token itself appeared in `launchSettings.json`,
shell history, a screenshot or a shared message, revoke that token instead. Revocation also invalidates
every registration derived from it; remove the now-useless local `credentials.json`, create a
replacement Developer token and perform the one-time enrollment again.

## See also

- [Plugin hosting](/sdk/hosting/) - registration modes, supervisor environment and reserved routes.
- [Authentication](/sdk/authentication/) - interactive pairing, Developer tokens, persisted
  credentials and session authentication.
- [Logging and health](/sdk/logging/) - forwarding behaviour, fallback logs and supervisor probes.
- [`macrodeck-plugin run`](/cli/run/) - every option and its exact process behaviour.
- [Troubleshooting](/guides/troubleshooting/) - protocol, analyzer, packaging and runtime error codes.
