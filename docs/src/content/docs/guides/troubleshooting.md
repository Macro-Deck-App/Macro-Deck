---
title: Troubleshooting
description: Symptom-driven fixes for the failures a Macro Deck plugin actually hits - build, packaging, connection, runtime and installation - each tied to a real diagnostic.
---

Find the symptom, read the cause, apply the fix. For breakpoints and IDE launches, see
[Debugging plugins](/guides/debugging/).

## First steps

```bash
macrodeck-plugin validate --directory bin/Release/net10.0
```

Anything manifest-shaped. Reports every problem in one run; exit `1` is a bad manifest, `3` an unreadable one.

```bash
macrodeck-plugin inspect --artifact com.example.my-plugin-1.0.0.macroDeckPlugin
```

What installing an artifact would find: entrypoints per RID, compatibility, signature shape.

```bash
macrodeck-plugin run --project src/MyPlugin --stub-host
```

Anything connection-shaped. `Session established` means registration, session and WebSocket all work.

```bash
tail -f ~/Library/Application\ Support/MacroDeck/logs/host-$(date +%Y%m%d).log
```

The host log, which includes your plugin's forwarded lines. Windows: `%APPDATA%\MacroDeck\logs`, Linux:
`~/.local/share/MacroDeck/logs`. Check the file names in that folder; see [where the lines end up](/features/logging/#where-the-lines-end-up).

## Installing the CLI

### `dotnet tool install` finds no package

Only `-preview` versions are published before 3.0, and `dotnet tool install` skips prereleases.

```bash
dotnet tool install --global MacroDeck.Plugin.Cli --prerelease
```

### `run` or `test` reports it cannot find `Microsoft.AspNetCore.App`

`run` and `test` start a real Kestrel loopback host, so they need the ASP.NET Core shared framework. Install
the ASP.NET Core runtime, or the .NET SDK.

### A mistyped command prints one confusing line

```text
$ macrodeck-plugin pakc --source .
error unknown-command: 'pakc' is not a macrodeck-plugin command. Did you mean 'pack'?
```

Not a fault: one line and exit `2` (usage error), not one error per option after the typo.

## Building the plugin

### `Build()` throws `PluginConfigurationException`

```text
The plugin is not configured correctly:
  - <problem>
  - <problem>
```

`Build()` validates everything local and throws once with every problem: a missing or malformed plugin id,
an illegal or duplicated capability id, a route under `/_macrodeck`, a missing or unreadable icon, a service
graph that will not resolve. Read the whole list and fix it in one pass. Connectivity is never checked here:
an absent host is a retry, not a configuration error.

### `Build()` fails naming `manifest.json`

The SDK reads the manifest from the content root and it is not there: the `Content` item is missing, or
you ran the executable from another working directory.

```xml
<ItemGroup>
    <Content Include="manifest.json" CopyToOutputDirectory="PreserveNewest" />
    <Content Include="Assets\icon.svg" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

Running a built plugin by hand from elsewhere needs `--contentRoot <dir>`: the content root defaults to the
working directory, not the executable's folder.

### `WithId` / `WithName` / `WithVersion` / `WithDescription` / `WithIcon` do not compile

They were removed. Move the values into `manifest.json` as `id`, `name`, `version`, `description` and
`icon`, and add the `Content` items above.

### Two integrations declare the same action id

On the wire an action belongs to the plugin, so its id must be unique across every integration in the
process. `Build()` fails naming the id; [MDP2001](/reference/analyzers/#mdp2001) catches constant ids at
compile time. Rename one.

### The analyzers flag something you did not expect

| Id | Meaning |
| --- | --- |
| [MDP1001](/reference/analyzers/#mdp1001) | `manifest.json` declares no usable `id`, or no `name`/`version`. |
| [MDP1003](/reference/analyzers/#mdp1003) | The manifest's `icon` has an extension with no known media type. |
| [MDP1004](/reference/analyzers/#mdp1004) | Your `IPluginIntegration` restates manifest-owned identity. |
| [MDP2005](/reference/analyzers/#mdp2005) | You mapped a route under the reserved `/_macrodeck` prefix. |
| [MDP3002](/reference/analyzers/#mdp3002) | `.Result`, `.Wait()`, `.GetAwaiter().GetResult()` or `Thread.Sleep` in a handler type. |
| [MDP4001](/reference/analyzers/#mdp4001) | A singleton takes `ICapabilityInvocationContext`, which only resolves inside one invocation's scope. |
| [MDP4002](/reference/analyzers/#mdp4002) | You override your own listener URL - see [below](#the-supervisor-reports-the-plugin-unhealthy-but-it-is-running-fine). |
| [MDP5004](/reference/analyzers/#mdp5004) | An API whose declared removal version this SDK has reached. |

Each id can be suppressed or escalated on its own: `<NoWarn>MDP2003</NoWarn>`, `-warnaserror:MDP5001`.

## Validating and packaging

### `validate` reports an error

```text
error invalid-version: '1.0' is not a valid SemVer version. [/version]
warning unknown-permission: 'host:everything' is not a known permission. [/permissions/0]

com.example.my-plugin 1.0: 1 error(s), 1 warning(s).
```

`validate` prints `error <code>: <message>` lines on stdout, with a JSON pointer where there is one.

| Code | Meaning | Fix |
| --- | --- | --- |
| `manifest-not-found` (exit `3`) | No `manifest.json` at the path, or run from the wrong directory. | Point at the build output: `--manifest bin/Release/net10.0/manifest.json`. |
| `malformed` | Not valid JSON; the message names a 1-based line and position. | Fix the JSON there. |
| `invalid-version` | `version` is not SemVer 2.0: `"1.0"` and `"v1.0.0"` fail. | Use `1.0.0`. |
| `invalid-plugin-id` | `id` is not reverse-domain: two or more dot-separated segments, lowercase, no underscores. | `com.example.my-plugin`. |
| `id-mismatch`, `version-mismatch` | The manifest disagrees with the directories it is installed under. | Match `<id>/versions/<version>/`. |
| `unknown-permission` | A permission outside the vocabulary. Advisory: it still installs. | Check [the vocabulary](/reference/manifest/#permissions). |
| `file-missing`, `file-size-mismatch`, `file-digest-mismatch` | `files[]` disagrees with the disk. | Do not hand-write `files[]`; let `pack` recompute it. |
| `undeclared-file` | A file in the artifact that `files[]` omits; once present, `files[]` is a complete inventory. | Repack. |
| `not-an-artifact` (exit `3`) | `--artifact` is not a ZIP, often a `manifest.json`. | Use `--manifest`; the CLI suggests it. |
| `source-directory` | Pointed at a source tree: `manifest.json` next to a `.csproj` with no built entrypoint. | Build, then validate the output directory. |
| `no-selector`, `too-many-selectors` (from `inspect`) | `inspect` needs exactly one of `--artifact`/`--directory`, with no default. | Pass exactly one. |

```text
error not-an-artifact: '.../manifest.json' is not a .macroDeckPlugin artifact (not a ZIP archive). Did you mean validate --manifest?
```

### `pack` prints warnings

None changes the exit code.

| Warning | Meaning |
| --- | --- |
| `entrypoint-not-packed` | A declared RID's binary is not in the payload. Packing only your own platform is a legitimate intermediate state. |
| `source-looks-like-debug-build` | `--source` looks like `bin/Debug/...`. Pack a Release build for distribution. |
| `languages-recomputed` | The declared `languages` disagree with `Localization/*.resx`; the resource files win, and the message names both lists. |
| `plugin-id-generated` (from `run`) | No `--plugin-id` and no manifest id, so a development id was invented. |

### `pack` fails

| Code | Fix |
| --- | --- |
| `source-not-found` | Point `--source` at an existing payload directory. |
| `output-exists` | Add `--force`. |
| `source-entry-rejected` | Remove the symlink or unsafe path. |
| `limit-exceeded` | Trim the payload below the artifact size/entry limits. |
| `write-failed` | Check the output location is writable. |
| `manifest-invalid` | Fix the manifest; `pack` prints the same report as `validate` and writes nothing. |

### A signature stopped verifying after packing

`pack` recomputes `files[]` and passes an existing `signature` through, so a manifest signed before packing
no longer matches its own digest. Sign after packing; `pack --show-digest` and `inspect --show-digest` print
the exact bytes a signature covers.

## Starting and connecting

### The plugin starts but never connects, and keeps retrying

Expected: a plugin starts without a host and retries with full-jitter exponential backoff (1s initial, 30s
maximum, factor 2). To fail instead:

```json
{ "MacroDeck": { "Plugin": { "FailFastOnFirstConnect": true } } }
```

### `host-not-found`

```text
error host-not-found: No running Macro Deck host was found. Its loopback port file is written while the host runs and removed when it stops; none was readable at ... Start Macro Deck, pass --host-url <url>, or use --stub-host to run against a disposable stub host instead.
```

`run` finds the host through `macro-deck-host.port` (or `macro-deck-host-development.port`) in the system
temp directory, which exists only while the host runs.

```bash
macrodeck-plugin run --project src/MyPlugin --stub-host                        # no Macro Deck needed
macrodeck-plugin run --project src/MyPlugin --host-url http://127.0.0.1:5000   # a host run cannot discover
```

### `run --mode managed` is refused

```text
error managed-needs-stub-host: Managed mode needs a launch bootstrap token only a real supervisor can mint - run cannot manufacture one against a real host. Use --stub-host, or --mode self-registering (the default) against the running host.
```

Add `--stub-host`. Against a real host use the default `--mode self-registering`, or the direct-project
workflow in [Debugging plugins](/guides/debugging/), which keeps a Developer token out of history and the
process list.

### `developer-mode-disabled`

`run` asked the host and Developer Mode is off, so no approval prompt can appear. Turn it on under
**Settings > Developer**. Nothing restarts: `run` stays up and the plugin pairs on its next reconnect.

### `enrollment-token-required`

```text
error enrollment-token-required: --mode self-registering against a real host needs --enrollment-token when --pairing is off.
```

`--pairing false` with no `--enrollment-token`. Drop `--pairing false` and approve the prompt, or pass
`--enrollment-token`. An inherited `MACRO_DECK_PLUGIN_ENROLLMENT_TOKEN` is scrubbed from the child and does
not count. The option exposes the token in shell history, process lists and screenshots, so prefer the
masked one-time enrollment in [Debugging plugins](/guides/debugging/).

### `UNAUTHENTICATED` (HTTP 401)

```text
Authentication failed.
```

Deliberately identical for an unknown plugin id, a wrong secret, a spent or expired bootstrap token, a
revoked registration, an unknown or expired Developer token and a missing header. Check:

- the `X-MacroDeck-Plugin-Id` and `X-MacroDeck-Plugin-Secret` headers are present and spelled correctly;
- a self-registered secret file was not deleted;
- the registration was not revoked;
- a managed launch token is used within two minutes; otherwise it needs a fresh launch.

### `UNAUTHENTICATED` with HTTP 403

The request was not from loopback, looked like a browser, or its session token names another session. Run
the plugin on the host's machine: both listeners serve plugin endpoints, but only to loopback callers.

`403` also means **Developer Mode** is off. That gates pairing (see [Pairing](#pairing)),
`POST /api/plugins/registration`, and `POST /api/plugins/sessions` for a plugin that enrolled with a
Developer token or paired - not a plugin Macro Deck installed and launches. Such a refusal carries
`"reason": "developer_mode_disabled"` in `details`, and
[`GET /api/plugins/protocol`](/reference/authentication/) reports `pairing.developerModeEnabled` and
`enrollment.developerModeEnabled`. Turn Developer Mode on; no restart.

### `PLUGIN_ALREADY_REGISTERED` (HTTP 409)

```text
A plugin is already registered with this identity.
```

A registration exists, but this machine or directory lost its `credentials.json`. Reuse the stored secret,
or run the plugin with Developer Mode on and let the pairing prompt **replace the development credential**
(it rotates the secret and ends the old session) - see
[Interactive pairing](/reference/authentication/#self-registering-interactive-pairing) and
[Registration is refused as already registered](/guides/debugging/#registration-is-refused-as-already-registered).
`DELETE /api/plugins/registration/{pluginId}` (admin) still works but is no longer the recommended path.

### `PROTOCOL_VERSION_UNSUPPORTED` (HTTP 422, or close `4001`)

```text
The requested protocol version is not supported.
```

No version in common; `details` carry the host's `supportedMinimum` and `supportedMaximum`. Widen your
declared range or update the SDK. A `4001` close means `session.hello` asserted a version other than the
negotiated one, which the SDK never does: suspect a hand-rolled client.

### `RATE_LIMITED` (HTTP 429)

```text
Too many requests; retry after the given delay.
```

Too many enrollment or session attempts. Wait for `details.retryAfterSeconds` / `Retry-After`. Enrollment
shares one bucket; session exchange is keyed per plugin id. A reconnect loop without backoff keeps hitting it.

### The socket closes with code…

| Code | Meaning | What to do |
| --- | --- | --- |
| `1013` | `QUEUE_OVERFLOW`: you ignored `flow.pause` past `maxInboundQueueDepth`. | Honour backpressure; the SDK does. |
| `4000` | `SESSION_REPLACED`: another connection for this plugin arrived without `resumeSessionId`. | Expected when a second instance starts; `maxSessionsPerPlugin` is 1. |
| `4001` | `PROTOCOL_VERSION_UNSUPPORTED`. | See above. Fatal. |
| `4002` | `SESSION_EXPIRED`. | Retryable: open a fresh session. |
| `4003` | Authentication failed. | Retryable up to `MaxAuthenticationFailures` (default 3), then fatal. |
| `4004` | `SupervisorShutdown`: the supervisor is stopping you. | Not an error; clean up within the grace period. |
| `4005` | `RegistrationRejected`: invalid or duplicated declared ids, or a colliding integration id. | Terminal. Fix the declaration and reconnect. |

### `UNKNOWN_MESSAGE_TYPE` or `MALFORMED_ENVELOPE` in the logs

Neither closes the connection. `UNKNOWN_MESSAGE_TYPE` is a peer speaking a newer catalogue.
`MALFORMED_ENVELOPE` is oversize input, excessive depth or a missing `type`. Suspect a hand-rolled client,
especially one that emits numbers as strings (`"deadlineMs": "30000"`), a hard failure here.

## Pairing

Symptoms of the default flow in
[Interactive pairing](/reference/authentication/#self-registering-interactive-pairing).

### No approval prompt appears

Developer Mode is off, or `MACRO_DECK_PLUGIN_HOST_URL` points at another host instance. While Developer
Mode is off, `POST /api/plugins/pairing` and redemption answer `403 UNAUTHENTICATED`, even for a request
approved earlier. Turn it on and check the host URL; the plugin keeps retrying and pairs without a restart.
Once a request is pending the plugin writes to stderr:

```text
Waiting for pairing approval in Macro Deck...
```

### The prompt was rejected

Rejection is fatal by design: the SDK pairs at most once per process, so it never re-prompts. Restart the
plugin process for a fresh request.

### The pairing request expired

It outlived its `expiresAt`. Requests live in memory only, so a host restart expires every pending or
approved one. Restart the plugin process and approve in time.

### `429` on `POST /api/plugins/pairing`

This plugin id already has a live request (a second does not replace it), the global pending cap was hit,
or the endpoint rate limit was. Resolve the existing request first; honour `Retry-After`.

### The host does not support pairing

`GET /api/plugins/protocol` has no `pairing` block: the host predates pairing. Use a Developer token -
see [Advanced: enroll with a Developer token for headless runs](/guides/debugging/#advanced-enroll-with-a-developer-token-for-headless-runs).

## Running

### `InitializeAsync` runs more than once, or not at process start

Initialization waits for a connection, because `IIntegrationContext` calls the host. It runs once per
session: a resume is a no-op, a non-resume reconnect shuts every integration down and re-initialises it.
Make `InitializeAsync` safely repeatable.

### State corrupts under load, or a handler behaves as if re-entered

Invocations run concurrently, up to `maxConcurrentInvocations` (32); earlier SDKs ran them one at a time.
Synchronise state shared across invocations. Each invocation gets its own DI scope, so scoped services are
already isolated.

### The UI shows a stale catalogue after something changed

`GetInstances`, `GetProfiles`, `DeclaredVariables`, `EventDefinitions` and similar are served from a cached
`describe`. Tell the host when they change outside a host-initiated invocation:

```csharp
notifier.CatalogChanged(kind); // IPluginCatalogNotifier, injected
```

Fire-and-forget, never throws, a no-op before a session exists.

### The supervisor reports the plugin unhealthy but it is running fine

Almost always an overridden listener URL. The supervisor binds a port before your process starts and
passes it as `ASPNETCORE_URLS`; a URL in `appsettings.json`, a launch profile or `UseUrls` listens where
nobody probes, silently. Remove it; [MDP4002](/reference/analyzers/#mdp4002) catches the visible cases.

Otherwise check that `health.path` matches the route you serve (default `/_macrodeck/health`) and that
`health.timeoutSeconds` is long enough. `unhealthyThreshold` has a floor of 2, so one missed probe never
restarts anything.

```bash
curl http://127.0.0.1:<port>/_macrodeck/diagnostics
```

### The plugin is killed at shutdown instead of exiting cleanly

```text
The plugin did not exit within its 10s grace period; killing it.
```

It missed `shutdown.gracefulTimeoutSeconds` (default 10, clamped 1-60) after `session.goodbye` and the
`4004` close. Make `ShutdownAsync` fast: stop new work, drain what is bounded, release. Lines logged at the
very end may be lost - see [logging](/features/logging/#shutdown).

### Your logs never reach the host's log viewer

Check in order:

1. `UseMacroDeckLogging()` is called.
2. The level is at or above `MacroDeck:Plugin:Logging:MinimumLevel` (default `Information`, independent of
   the pipeline's own minimum).
3. The plugin is connected: `log.publish` has no replay, so a batch lost in an outage is gone.
4. You are under the ingestion rate limit, which drops excess events without closing the session.

See [logging](/features/logging/).

## Installing an artifact

### The install API returns an error code

| Code | Cause | Fix |
| --- | --- | --- |
| `invalid_archive` | Not a readable ZIP. | Repack. |
| `unsafe_entry` | An absolute path, drive letter, `..` segment, reserved Windows device name, illegal filename character, or symlink/fifo/socket/device node. | Remove it - see [what the installer rejects](/reference/plugin-hosting/#the-macrodeckplugin-artifact). |
| `artifact_too_large`, `artifact_limit_exceeded` | Entry count, byte totals or compression ratio over the limits. | Trim the payload. |
| `manifest_missing` | No `manifest.json` at the archive root (the version directory, no wrapper folder). | Repack with `macrodeck-plugin pack`. |
| `manifest_invalid` | The reader rejected the manifest. | Run `macrodeck-plugin validate --artifact …`. |
| `id_mismatch` | The manifest names a different plugin than the caller asked to install. | Check `id`. |
| `incompatible` | `compatibility.protocol` or `compatibility.macroDeck` excludes this host; a rejection, not a warning. | Widen the range, or use a matching host. |
| `hash_mismatch` | A declared file digest, or the artifact's expected hash, did not match. | Repack; never edit an artifact in place. |
| `signature_invalid` | The signature block is malformed: no `keyId`, non-base64 `value`, or an `ed25519` value that is not 64 bytes. | Fix or remove the block. |
| `already_installed` | That version is on disk. | Pass `force`; it does not bypass compatibility, digests or signature rejection. |
| `health_validation_failed` | The version never became healthy, so activation was rolled back and the version deleted. | Fix health (usually [the listener URL](#the-supervisor-reports-the-plugin-unhealthy-but-it-is-running-fine)) and reinstall. |
| `dependency_in_use` | Another plugin hard-depends on the one being uninstalled. | Remove the dependent first, or force it. |
| `no_artifact` | `install` or `inspect` was called with no file. | Attach the file. |
| `DesktopOnly` | A local-path endpoint was called outside the desktop app (shared portability refusal, hence PascalCase). | Use the upload endpoint. |

### It installed, but it will not start

- **A missing hard dependency or a live conflict.** A *Blocking* warning withholds the automatic start.
  Install what is missing, or start it manually.
- **No entrypoint for this host's RID.** The only fallbacks are `osx-arm64 → osx-x64` and
  `win-arm64 → win-x64`; there is no `"any"` key and `linux-musl-*` resolves nothing. The installer adds
  an advisory warning without health-gating. Check with `inspect`: the RID shows `(missing)`.

### A framework-dependent plugin reports a missing runtime

No `dotnet` muxer (searched in `DOTNET_ROOT`, then `PATH`, then well-known locations), or no installed
`Microsoft.NETCore.App` with the entrypoint's `dotnetVersion` major and at least its minor. A higher major
never satisfies a lower one. Install the matching runtime; the plugin stays stopped instead of burning
restart budget.

## Signing and install trust

### The install is refused with a signature error

The host verifies in a staging directory before writing anything; every verdict but unsigned refuses.

| Verdict | Meaning |
| --- | --- |
| `SignatureInvalid` | The block is malformed, does not verify, or the contents do not match it. |
| `SignatureUntrusted` | The certificate does not chain to the pinned root, has another purpose, or was not valid at `signedAt`. |
| `SignatureRevoked` | The certificate is revoked. |
| `SignatureUnverifiable` | The package could not be read, or its algorithm is unknown to this host. |

```bash
macrodeck-plugin verify ./downloads/com.example.my-plugin-1.0.0.macroDeckPlugin
```

[`verify`](/cli/signing/#verify) names the failing check. Reinstall from a good copy: a signature that does
not verify is never treated as unsigned, so no confirmation installs it.

### An unsigned plugin will not install

`UnsignedNotPermitted`. Unsigned packages install only with an explicit per-install confirmation, for a file
you selected or uploaded - never from a store or registry. A plugin id previously admitted as signed never
accepts an unsigned package. Install from a local file and confirm, or install a signed build.

### A plugin that used to work now refuses to launch

Installed plugins are re-verified on every launch, because the plugin directory is user-writable. Editing
files or stripping the certificate breaks it, and the refusal lasts for the process lifetime. Reinstall
from a good artifact over the existing one - see
[signing](/policies/security/#signing-the-creator-portal-signs-and-the-host-verifies-before-install-and-before-every-load).

## See also

- [Plugin CLI](/cli/) - every command, diagnostic and exit code.
- [Debugging plugins](/guides/debugging/) - breakpoints, IDE profiles, enrollment tokens.
- [Logging](/features/logging/) - log locations, health routes, shutdown.
- [Plugin hosting](/reference/plugin-hosting/) - artifact format, supervision and shutdown.
- [Authentication](/reference/authentication/) - credentials, session exchange and their errors.
- [Analyzers](/reference/analyzers/) - every diagnostic.
- [Conformance](/reference/conformance/) - a verdict rather than a symptom.
