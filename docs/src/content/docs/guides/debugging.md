---
title: Debugging plugins
description: Hit a breakpoint in a .NET plugin from Rider, Visual Studio or VS Code, against the desktop app or a disposable stub host.
---

Put a breakpoint in your plugin and run it under your IDE, against the desktop app or a throwaway stub host.

## Quick start

`macrodeck-plugin new` already generates this profile in `src/<Name>/Properties/launchSettings.json`:

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
        "DOTNET_ENVIRONMENT": "Development",
        "MACRO_DECK_PLUGIN_MODE": "SelfRegistering",
        "MACRO_DECK_PLUGIN_HOST_URL": "http://127.0.0.1:8193",
        "MACRO_DECK_PLUGIN_STATE_DIRECTORY": ".macrodeck-dev-state"
      }
    }
  }
}
```

1. Start the Macro Deck desktop app and turn on **Developer Mode** in its settings (off by default).
2. Open the solution, pick **Macro Deck - Real Host**, and press F5 in Rider or Visual Studio (VS Code:
   see [below](#vs-code)).
3. Approve the pairing prompt in the desktop app.

The plugin asks the desktop app to pair, and after you approve it stores its own credential in
`.macrodeck-dev-state/`. Later runs connect without a prompt.

## Run against the desktop app

Use the Quick start profile, or from the plugin project directory:

```bash
dotnet run --launch-profile "Macro Deck - Real Host"
```

- The desktop app must run on the same machine: plugin endpoints reject non-loopback callers.
- `SelfRegistering` is the only mode an independently started plugin can use against a real host. Managed
  mode needs a bootstrap token only the real supervisor can mint.
- `MACRO_DECK_PLUGIN_HOST_URL` is the **host's** address, not the plugin's own listener.
- `.macrodeck-dev-state` resolves against the profile's working directory, the project directory.
- Never set `ASPNETCORE_URLS`, `applicationUrl`, `UseUrls(...)` or a `urls` value. The SDK binds to
  `http://127.0.0.1:0`; an installed plugin's listener is chosen and probed by the supervisor.
  [MDP4002](/reference/analyzers/#mdp4002) catches the visible forms.

Breakpoints in `Program.cs` hit at process start. `IPluginIntegration.InitializeAsync` runs only once a
session opens, and action handlers only when triggered from Macro Deck.

## Run against the stub host

```bash
macrodeck-plugin run --project . --stub-host
```

```text
Started a disposable stub host at http://127.0.0.1:59826.
Started process 38264 (mode: SelfRegistering, host: http://127.0.0.1:59826). Press Ctrl-C to stop.
[plugin] info: Microsoft.Hosting.Lifetime[14]
[plugin]       Now listening on: "http://127.0.0.1:59827"
...
[plugin]       Registered with the host as '"com.example.demo"'.
...
[plugin]       Initialized.
```

No Macro Deck install, no Developer Mode, no pairing. The stub is a real in-process `MacroDeckTestHost`
with the real registration, session and WebSocket implementation, and `run` composes the same
environment as the supervisor. Use it for registration, reconnect and shutdown; use the desktop app when
the real UI matters. Without `--stub-host`, `run` connects to the running desktop app instead. Every
option is in [`macrodeck-plugin run`](/cli/run/).

## Attach to a process started by `run`

`run` is the parent; your plugin is a separate .NET child. Debugging the CLI alone binds nothing.

1. Start `run` and note the PID in `Started process <pid>`.
2. Attach the IDE's .NET (CoreCLR) debugger to that PID. A framework-dependent plugin may show as
   `dotnet`: match the PID, or the command line ending in your plugin's DLL.

`run --project` makes a normal Debug build, so the PDB is present. A hollow breakpoint usually means you
attached to the CLI or to a previous run's child.

## VS Code

`.vscode/launch.json`, with the C# extension, after `dotnet build`:

```json
{
  "version": "0.2.0",
  "configurations": [
    {
      "name": "Macro Deck - Real Host",
      "type": "coreclr",
      "request": "launch",
      "program": "${workspaceFolder}/src/Demo/bin/Debug/net10.0/Demo.dll",
      "cwd": "${workspaceFolder}/src/Demo",
      "env": {
        "DOTNET_ENVIRONMENT": "Development",
        "MACRO_DECK_PLUGIN_MODE": "SelfRegistering",
        "MACRO_DECK_PLUGIN_HOST_URL": "http://127.0.0.1:8193",
        "MACRO_DECK_PLUGIN_STATE_DIRECTORY": ".macrodeck-dev-state"
      }
    },
    {
      "name": "Attach to plugin",
      "type": "coreclr",
      "request": "attach",
      "processId": "${command:pickProcess}"
    }
  ]
}
```

Replace `Demo` with your project name. Keep `cwd` on the project directory: the SDK reads
`manifest.json` from there. Use **Attach to plugin** for a child started by `run`.

## Rider

- **Desktop app:** choose the **Macro Deck - Real Host** launch profile and Debug.
- **Stub host:** create a .NET Executable configuration for `macrodeck-plugin` with arguments
  `run --project . --stub-host`, working directory the plugin project, and enable **Attach to child .NET
  processes**. The tool must be on Rider's `PATH`, or use its absolute path and keep that change
  private.

## Visual Studio

- **Desktop app:** select **Macro Deck - Real Host** in the start button's drop-down and press F5.
- **Stub host:** start `run` in a terminal, then **Debug → Attach to Process** and pick the child PID.

## Developer Mode, pairing and Developer tokens

- **Developer Mode** gates pairing, enrolment and every session for a development credential. It
  persists, and toggling it takes effect without restarting the plugin or Macro Deck.
- The SDK creates the pairing request, waits for approval, redeems it and writes the per-plugin secret
  to `.macrodeck-dev-state/<plugin-id>/credentials.json` before using it. The file also records the
  issuing host. It is owner-only on Unix, inherits the directory ACL on Windows, and is **not encrypted
  at rest**.
- The generated `.gitignore` already ignores `**/.macrodeck-dev-state/`. Keep that rule: the directory
  holds the long-lived secret and fallback logs. `launchSettings.json` holds no secret and can be
  committed.

Full protocol: [Authentication](/reference/authentication/).

### Advanced: enroll with a Developer token for headless runs

For CI and other runs with nobody to approve a prompt. It replaces the prompt, not Developer Mode, and is
needed only while the state directory has no stored credential.

Create a token under **Developer Tools → Plugin development → Credentials**. The plaintext is shown once.
Run once from a terminal with the token only in the environment (zsh):

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

Bash: `read -rsp "Enrollment token: " MACRO_DECK_PLUGIN_ENROLLMENT_TOKEN`. PowerShell 7:

```powershell
$env:MACRO_DECK_PLUGIN_ENROLLMENT_TOKEN = Read-Host "Enrollment token" -MaskInput
$env:MACRO_DECK_PLUGIN_MODE = "SelfRegistering"
$env:MACRO_DECK_PLUGIN_HOST_URL = "http://127.0.0.1:8193"
$env:MACRO_DECK_PLUGIN_STATE_DIRECTORY = ".macrodeck-dev-state"
dotnet run
Remove-Item Env:MACRO_DECK_PLUGIN_ENROLLMENT_TOKEN, Env:MACRO_DECK_PLUGIN_MODE, Env:MACRO_DECK_PLUGIN_HOST_URL, Env:MACRO_DECK_PLUGIN_STATE_DIRECTORY
```

Stop once `/_macrodeck/ready` returns 200 or the log says it registered. The SDK has swapped the token
for a per-plugin secret in the same `credentials.json`; later IDE runs need no token.

:::caution
Never put the token in `launchSettings.json`, User Secrets, CLI arguments or other persisted config:
shell history, process lists and screenshots expose it. Discard the plaintext, but keep the token
**active** in Macro Deck: revoking or letting it expire stops every registration it enrolled. Removing a
revoked token from the list also deletes those registrations. Don't delete `credentials.json` to get rid
of the token - it holds the separate per-plugin secret.
:::

## Read logs while debugging

| Where | What you see |
| --- | --- |
| IDE console (direct launch) | Startup, unhandled failures, the plugin's own log output. |
| `macrodeck-plugin run` | `[plugin]` (stdout) and `[plugin:stderr]` (stderr) lines; against the stub also `[log:...]` forwarded events. |
| Desktop app log viewer | Events forwarded by `.UseMacroDeckLogging()` (the template calls it), default minimum `Information`. |
| `.macrodeck-dev-state/<plugin-id>/logs/plugin-fallback.log` | The bounded tail of batches that could not be delivered. Never replayed. |

`.UseMacroDeckLogging()` keeps writing to the console too, so an extra Serilog console sink prints every
line twice. Limits and levels: [Logging and health](/features/logging/).

The plugin's own routes, on its `Now listening on` port (never `8193`, which is the host):

```bash
curl -i http://127.0.0.1:<plugin-port>/_macrodeck/health       # 200 once serving: liveness only
curl -i http://127.0.0.1:<plugin-port>/_macrodeck/ready        # 200 once a session is open, else 503
curl -s http://127.0.0.1:<plugin-port>/_macrodeck/info         # identity, mode, SDK and protocol version
curl -s http://127.0.0.1:<plugin-port>/_macrodeck/diagnostics  # status, faultReason, session, reconnects, close code
```

## Common first-run failures

| Symptom | Fix |
| --- | --- |
| No pairing prompt | Turn on Developer Mode (no restart needed; the plugin pairs on its next reconnect and says so on stderr). Check `MACRO_DECK_PLUGIN_HOST_URL` points at this desktop app. Prompts appear only in the desktop app, with a system notification and a notification-list entry. |
| `health` 200, `ready` 503 | No session. Read `diagnostics.status` and `faultReason`; check the host runs, the URL is loopback and the credential belongs to this host. An unreachable host is retried with backoff, not a startup error. |
| "No stored credential and no enrollment token" | Headless path only: token exported in the same shell, exact name `MACRO_DECK_PLUGIN_ENROLLMENT_TOKEN`, mode `SelfRegistering` with the loopback host URL, working directory the project. |
| Already registered (`409`) | See [below](#registration-is-refused-as-already-registered). |
| WebSocket closes with `4000` (`SESSION_REPLACED`) | One live session per plugin id: stop the installed instance or the other debug run. |
| Installed plugin reported unhealthy | Remove every listener override; check the manifest's `health.path` (default `/_macrodeck/health`) and timeouts. |
| Manifest not found | The SDK reads `manifest.json` from the content root: keep it at the project root and copy it to output (below). Don't override the profile's working directory with an absolute path. |
| Breakpoint in `InitializeAsync` never hits | Check `ready` and `diagnostics`: the process may still be reconnecting. |
| Logs local but not in the host viewer | `.UseMacroDeckLogging()` called, `ready` is 200, event meets `MacroDeck:Plugin:Logging:MinimumLevel`; then read `plugin-fallback.log`. |

```xml
<ItemGroup>
    <Content Include="manifest.json" CopyToOutputDirectory="PreserveNewest" />
    <Content Include="Assets\icon.svg" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

More symptoms, including rejected or expired pairing and `429`: [Troubleshooting](/guides/troubleshooting/#pairing).

### Registration is refused as already registered

The host knows this plugin id, but your `credentials.json` is gone (deleted state directory, fresh clone).
Run again with Developer Mode on and approve **replace the development credential** in the prompt. The
host rotates in the new secret and ends the old sessions; no manual revocation.

### If credentials leak

- **`credentials.json` committed or shared:** delete it locally, run again and approve **replace the
  development credential**. Removing it from the next commit is not enough.
- **Developer token exposed:** revoke it (which invalidates its registrations), delete `credentials.json`,
  create a new token and enroll again.

## See also

- [Plugin hosting](/reference/plugin-hosting/) - registration modes and every `MACRO_DECK_PLUGIN_*` variable.
- [Authentication](/reference/authentication/) - pairing, Developer tokens, stored credentials.
- [Logging and health](/features/logging/) - forwarding, fallback log, health probes.
- [`macrodeck-plugin run`](/cli/run/) - every option and exit code.
- [Troubleshooting](/guides/troubleshooting/) - error codes by stage.
