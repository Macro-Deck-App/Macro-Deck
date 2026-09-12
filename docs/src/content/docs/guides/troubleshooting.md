---
title: Troubleshooting
description: Symptom-driven fixes for the failures a Macro Deck plugin actually hits - build, packaging, connection, runtime and installation - each tied to a real diagnostic.
---

Every entry below is tied to something checkable: a CLI diagnostic code, an analyzer id, a protocol
error code, a WebSocket close code, or a rule the manifest reader enforces. If your symptom is not here,
the fastest next step is usually `macrodeck-plugin validate` for anything manifest-shaped and
`macrodeck-plugin run` for anything connection-shaped.

For an interactive investigation with IDE breakpoints, a real desktop host or a CLI child process,
start with [Debugging plugins](/guides/debugging/).

## Tooling

### `dotnet tool install` finds no package

**Cause.** Only `-preview` versions are published ahead of the 3.0 release, and `dotnet tool install`
ignores prerelease versions unless asked.

**Fix.** Add `--prerelease`:

```bash
dotnet tool install --global MacroDeck.Plugin.Cli --prerelease
```

### `run` or `test` reports it cannot find `Microsoft.AspNetCore.App`

**Cause.** The CLI's `run` and `test` commands start a real Kestrel loopback test host, so they need the
ASP.NET Core shared framework, not just the .NET runtime.

**Fix.** Install the ASP.NET Core runtime, or the .NET SDK, alongside the .NET runtime.

### A mistyped command prints one confusing line

Not a fault. `macrodeck-plugin pakc --source .` reports
`'pakc' is not a macrodeck-plugin command. Did you mean 'pack'?` and exits `2` (usage error), rather
than one unrecognised-token error per option that followed the typo.

## Building the plugin

### `Build()` throws `PluginConfigurationException`

**Cause.** `Build()` validates everything it can locally and throws **once**, carrying *every* problem
it found - a missing or malformed plugin id, an illegal or duplicated capability id, a route under the
reserved `/_macrodeck` prefix, a missing or unreadable icon, a service graph that will not resolve.

**Fix.** Read the whole exception, not the first line. Fixing a plugin should take one run, not one run
per mistake. Note that nothing about connectivity is validated here: a host that is not there is a
retry, not a configuration error.

### `Build()` fails naming `manifest.json`

**Cause.** The SDK reads the manifest from the plugin's **content root**, and it is not there. Either
the `Content` item is missing from the project file, or you are running the built executable from a
different working directory.

**Fix.** Add the items:

```xml
<ItemGroup>
    <Content Include="manifest.json" CopyToOutputDirectory="PreserveNewest" />
    <Content Include="Assets\icon.svg" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

Running a built plugin by hand from elsewhere needs `--contentRoot <dir>`, the standard ASP.NET Core
switch: the content root defaults to the working directory, not the executable's folder.

### `WithId` / `WithName` / `WithVersion` / `WithDescription` / `WithIcon` do not compile

**Cause.** They are gone. Each duplicated a field the manifest already carried, in a second place that
could disagree with the first.

**Fix.** Move all five values into `manifest.json` as `id`, `name`, `version`, `description`, `icon`,
and add the `Content` items above.

### Two integrations declare the same action id

**Cause.** In process, an action is identified by (integration id, action id), so two integrations may
each declare `play`. Over the wire the owner is the **plugin**, so an action id has to be unique across
every integration in the process.

**Fix.** Rename one. The collision fails `Build()` with the id named rather than making one of the two
unreachable. [MDP2001](/reference/analyzers/#mdp2001) reports two statically visible declarations of
one capability kind sharing a constant id, so the common case is caught at compile time.

### The analyzers flag something you did not expect

| Id | What it means |
| --- | --- |
| [MDP1001](/reference/analyzers/#mdp1001) | `manifest.json` declares no usable `id`, or no `name`/`version` |
| [MDP1003](/reference/analyzers/#mdp1003) | The manifest's `icon` has a file extension with no known media type |
| [MDP1004](/reference/analyzers/#mdp1004) | Your `IPluginIntegration` restates manifest-owned identity - it must not |
| [MDP2005](/reference/analyzers/#mdp2005) | You mapped a route under the reserved `/_macrodeck` prefix |
| [MDP3002](/reference/analyzers/#mdp3002) | `.Result`, `.Wait()`, `.GetAwaiter().GetResult()` or `Thread.Sleep` in a handler type |
| [MDP4001](/reference/analyzers/#mdp4001) | A singleton whose constructor takes `ICapabilityInvocationContext`, which only resolves inside one invocation's scope |
| [MDP4002](/reference/analyzers/#mdp4002) | You are overriding your own listener URL - see [the health section below](#the-supervisor-reports-the-plugin-unhealthy-but-it-is-running-fine) |
| [MDP5004](/reference/analyzers/#mdp5004) | Use of an API whose declared removal version this SDK has already reached |

Every diagnostic has a stable id and can be suppressed or escalated independently -
`<NoWarn>MDP2003</NoWarn>`, `-warnaserror:MDP5001`.

## Manifest and packaging

`macrodeck-plugin validate` reports every independent problem in one run, as `error <code>: <message>`
lines on **stdout** with a schema pointer where there is one. Exit `1` means the manifest was read and
found wanting; exit `3` means it could not be read at all.

| Symptom | Likely cause | Fix |
| --- | --- | --- |
| `manifest-not-found`, exit `3` | No `manifest.json` at the path given, or `validate` was run from the wrong directory | Run it against your build output, e.g. `--manifest bin/Release/net10.0/manifest.json` |
| `malformed`, naming a line and position | The file is not valid JSON | Fix the JSON at the reported 1-based line/position |
| `invalid-version` | `version` is not SemVer 2.0 - `"1.0"` and `"v1.0.0"` both fail | Use `1.0.0` |
| An id complaint | `id` is not a reverse-domain id: it needs at least two dot-separated segments, lowercase only, no underscores | `com.example.my-plugin` |
| An id/version mismatch | The manifest disagrees with the directory names it was installed under | Make `id` and `version` match `<id>/versions/<version>/` |
| `unknown-permission` | A permission string outside the known vocabulary | Advisory only - it still installs. Check the spelling against [the vocabulary](/reference/manifest/#permissions) |
| `file-missing`, `file-size-mismatch`, `file-digest-mismatch` | `files[]` disagrees with what is on disk | Do not hand-write `files[]`. Let `pack` recompute it |
| `undeclared-file` | A file in the artifact that `files[]` does not list. Once the list exists at all it is a complete inventory | Repack |
| `'<path>' is not a .macroDeckPlugin artifact (not a ZIP archive)` | `--artifact` was pointed at something else - often a `manifest.json` | Use `--manifest` instead; the CLI suggests this when the file is named `manifest.json` |
| A "wrong directory" diagnosis | `validate`/`pack` was pointed at a source tree - a `manifest.json` next to a `.csproj` with no built entrypoint | Build the project and point at the output directory |
| `no-selector` / `too-many-selectors` from `inspect` | `inspect` needs **exactly one** of `--artifact`/`--directory` - unlike `validate`, which defaults to `./manifest.json` | Pass exactly one |

### `pack` warnings that are not failures

| Warning | Meaning |
| --- | --- |
| `entrypoint-not-packed` | The manifest declares an entrypoint for a RID whose binary is not in the payload. Packing only the platform you are building on is a legitimate intermediate state; exit code stays `0` |
| `source-looks-like-debug-build` | `--source` looks like `bin/Debug/...`. Pack a Release build for distribution |
| `languages-recomputed` | The manifest declared a `languages` list that disagrees with the project's `Localization/*.resx` set. The resource files win; the message names both lists |
| `plugin-id-generated` (from `run`) | No `--plugin-id` and no manifest id was found, so a development id was invented. The message names the way out |

### `pack` failures

`source-not-found`, `output-exists` (add `--force`), `source-entry-rejected` (a symlink or an unsafe
path), `limit-exceeded` (an artifact size/entry limit), `write-failed`, `manifest-invalid` (the same
validation `validate` runs, which stops `pack` before a byte is written).

### A signature stopped verifying after packing

**Cause.** `pack` recomputes `files[]` from disk and passes any existing `signature` through untouched,
so a manifest signed *before* packing carries a signature that no longer matches its own digest.

**Fix.** Sign after packing, against the packed artifact's manifest. `pack --show-digest` and
`inspect --show-digest` print the exact bytes a signature covers.

## Connecting to a host

### The plugin starts but never connects, and keeps retrying

Expected behaviour, not a failure: a plugin starts even when the host is unreachable and retries with
full-jitter exponential backoff (1s initial, 30s maximum, factor 2). Set
`MacroDeck:Plugin:FailFastOnFirstConnect` to `true` if you would rather it fail.

### `host-not-found`

**Cause.** `run` defaults to the running Macro Deck host and discovers it through the loopback port file
the host publishes while it runs (`macro-deck-host.port`, or `macro-deck-host-development.port` for a
Development build, in the system temp directory). No readable port file means no running host - the host
deletes the file when it stops.

**Fix.** Start Macro Deck, or add `--stub-host` to run against a disposable stub host instead, which
needs no installation at all. Pass `--host-url <url>` for a host whose port file `run` cannot see - a
host started with a different temp directory, for instance.

### `run --mode managed` is refused

**Cause.** `run` now targets the running host by default, and a managed launch needs a launch bootstrap
token only a real supervisor can mint, which `run` cannot manufacture against a host it did not start.

**Fix.** Add `--stub-host`; managed mode is a stub-host-only launch. Against a real desktop host, use the
default `--mode self-registering`, or the direct-project workflow in
[Debugging plugins](/guides/debugging/), which starts the plugin without putting a Developer token in
command history or the process list.

### `developer-mode-disabled`

**Cause.** `run` asked the host whether pairing is available and it answered that **Developer Mode** is
off, so the approval prompt it would otherwise have promised can never appear.

**Fix.** Turn Developer Mode on in the desktop app's settings. Nothing needs restarting and `run` does
not need re-running - it stays up, and the plugin pairs on its next reconnect tick.

### `enrollment-token-required`

**Cause.** Pairing was explicitly turned off with `--pairing false`, and no `--enrollment-token` was
given either. This is not what a self-registering run against a real host needs by default: `run` reports
what pairing will actually do - see [`run`](/cli/run/) - and lets the prompt in Macro Deck supply the
credential, rather than requiring a token up front.

**Fix.** Drop `--pairing false` and approve the prompt in Macro Deck, or supply `--enrollment-token` if
pairing genuinely needs to stay off. The CLI accepts the token only through `--enrollment-token`; an
inherited `MACRO_DECK_PLUGIN_ENROLLMENT_TOKEN` is deliberately scrubbed from the child environment and
does not satisfy the command. Because the option can expose the token through shell history, process
inspection or screenshots, prefer the masked one-time `dotnet run` enrollment in
[Debugging plugins](/guides/debugging/) for interactive real-host development.

### `UNAUTHENTICATED` (HTTP 401)

**Cause.** Deliberately indistinguishable. An unknown plugin id, a wrong secret, a spent or expired
bootstrap token, a revoked registration, an unknown or expired Developer token, and a missing header
all produce the byte-identical error, so there is no oracle to probe.

**Fix.** Work through the possibilities rather than expecting the error to narrow them. Check that the
credential headers are present and spelled correctly (`X-MacroDeck-Plugin-Id`,
`X-MacroDeck-Plugin-Secret`); that a self-registered secret file has not been deleted; and that the
registration has not been revoked. A managed launch token is valid for two minutes **while unused** -
a plugin that never connects within that window needs a fresh launch.

### `UNAUTHENTICATED` with HTTP 403

**Cause.** Same code, different status: the request did not arrive from a loopback address, or looked
like it came from a browser, or the session token names a different session. `403` also means
**Developer Mode** is off in the desktop app's settings. That applies to pairing (see
[Pairing](#pairing) below), to `POST /api/plugins/registration`, and to `POST /api/plugins/sessions`
for a plugin that enrolled with a Developer token or paired interactively - a plugin Macro Deck
installed and launches itself is unaffected. A refusal for that reason carries `reason:
"developer_mode_disabled"` in the error's `details`, and
[`GET /api/plugins/protocol`](/reference/authentication/) reports the switch in
`pairing.developerModeEnabled` and `enrollment.developerModeEnabled` so a plugin can check before it
tries.

**Fix.** Run the plugin on the same machine as the host. Plugin endpoints are served on both listeners
but only accept a loopback remote address - a LAN caller is refused regardless of port. For the
Developer Mode case, turn Developer Mode on; it takes effect immediately, with no restart.

### `PLUGIN_ALREADY_REGISTERED` (HTTP 409)

**Cause.** A registration already exists for that identity, but this machine or working directory no
longer has the matching `credentials.json`.

**Fix.** Reuse the stored secret if you still have it. Otherwise run the plugin again with Developer
Mode on: the pairing approval prompt detects the existing registration and offers to **replace the
development credential**, which rotates the secret host-side and terminates the old session - see
[Interactive pairing](/reference/authentication/#self-registering-interactive-pairing) and
[Registration is refused as already registered](/guides/debugging/#registration-is-refused-as-already-registered).
Manually revoking the registration (`DELETE /api/plugins/registration/{pluginId}`, an admin operation)
is still available but is no longer the recommended path.

### `PROTOCOL_VERSION_UNSUPPORTED` (HTTP 422, or close `4001`)

**Cause.** No protocol version in common. The error's `details` carry the host's `supportedMinimum` and
`supportedMaximum`.

**Fix.** Widen the range your plugin declares, or update the SDK. If it surfaces as a `4001` close
rather than a 422, `session.hello` asserted a version other than the one already negotiated - which the
SDK never does, so suspect a hand-rolled client.

### `RATE_LIMITED` (HTTP 429)

**Cause.** Too many enrollment or session attempts. `details.retryAfterSeconds` and the `Retry-After`
header say how long to wait. Enrollment shares one bucket; session exchange is keyed per plugin id.

**Fix.** Honour the delay. A reconnect loop that ignores backoff will keep hitting this.

### The socket closes with code…

| Code | Meaning | What to do |
| --- | --- | --- |
| `1013` | `QUEUE_OVERFLOW` - you ignored a `flow.pause` past `maxInboundQueueDepth` | Honour backpressure. The SDK does this for you |
| `4000` | `SESSION_REPLACED` - another connection for the same plugin arrived without `resumeSessionId` | Expected when a second instance starts. `maxSessionsPerPlugin` is 1 |
| `4001` | `PROTOCOL_VERSION_UNSUPPORTED` | See above. Fatal |
| `4002` | `SESSION_EXPIRED` | Retryable - open a fresh session |
| `4003` | Authentication failed | Retryable up to `MaxAuthenticationFailures` (default 3), then fatal |
| `4004` | `SupervisorShutdown` - the supervisor is stopping you deliberately | Not an error. Managed plugins stop the application; do cleanup inside the grace period |
| `4005` | `RegistrationRejected` - invalid or duplicated declared ids, or a colliding integration id | Terminal. Fix the declaration and reconnect |

### `UNKNOWN_MESSAGE_TYPE` or `MALFORMED_ENVELOPE` in the logs

Neither ever closes the connection. `UNKNOWN_MESSAGE_TYPE` is the designed outcome of a peer speaking a
newer catalogue; `MALFORMED_ENVELOPE` covers oversize input, depth violations and a missing `type`.
Suspect a hand-rolled client - in particular a JSON stack that emits numbers as strings
(`"deadlineMs": "30000"`), which is a hard failure here.

## Pairing

Symptoms specific to the default interactive pairing flow described in
[Interactive pairing](/reference/authentication/#self-registering-interactive-pairing).

### No approval prompt appears

**Cause.** Either **Developer Mode** is off in the desktop app's settings, or the plugin's
`MACRO_DECK_PLUGIN_HOST_URL` points at a different host instance than the one you are watching.
Developer Mode is a continuous condition, not just a creation-time gate: while it is off, both
`POST /api/plugins/pairing` and the redemption endpoint answer `403` with `UNAUTHENTICATED` - including
for a request that was already approved before Developer Mode was turned off.

**Fix.** Turn Developer Mode on, and confirm the host URL matches the desktop app you expect the prompt
to appear in. Nothing needs restarting: a plugin that found Developer Mode off keeps retrying and pairs
as soon as it is on. `macrodeck-plugin run` reports this case as `developer-mode-disabled` rather than
promising a prompt, and the plugin writes `Waiting for pairing approval in Macro Deck...` to stderr once
a request is actually pending.

### The prompt was rejected

**Cause.** A rejected pairing request is a fatal outcome by design: the SDK attempts pairing at most
once per process lifetime, and treats rejection the same as expiry or an unsupported host, rather than
retrying silently and re-prompting forever.

**Fix.** Fix whatever made rejection the right call, then restart the plugin process to create a fresh
pairing request. Waiting will not produce another prompt on its own.

### The pairing request expired

**Cause.** The request outlived its host-advertised `expiresAt` before it was approved and redeemed.
Requests are memory-only and bounded, so a host restart also expires every pending or already-approved
request regardless of how much time was left.

**Fix.** Restart the plugin process to create a new request, and approve it before it lapses.

### `429` on `POST /api/plugins/pairing`

**Cause.** Either this plugin id already has a live pairing request - creating a second one does not
replace the first - or the host's global cap on pending requests was reached, or the per-endpoint rate
limit was hit.

**Fix.** Approve or let the existing request for this plugin id resolve before creating another one.
Honour `Retry-After` if the cap or rate limit was the cause.

### The host does not support pairing

**Cause.** `GET /api/plugins/protocol` carries no `pairing` block. This is an older host that predates
interactive pairing.

**Fix.** Use a Developer token and the headless enrollment flow instead - see
[Advanced: enroll with a Developer token for headless runs](/guides/debugging/#advanced-enroll-with-a-developer-token-for-headless-runs).

## Running

### `InitializeAsync` runs more than once, or not at process start

**Cause.** Initialization is gated on the connection being established, not on
`IHostedService.StartAsync`, because `IIntegrationContext` makes real host callbacks that would fail
against a connection that does not exist yet.

**Fix.** Expect `InitializeAsync` to run once per session and make it safely repeatable. A resumed
session is a no-op; a non-resume reconnect shuts every integration down and re-initialises them,
because cached host-side state from the old session cannot be trusted.

### State corrupts under load, or a handler behaves as if re-entered

**Cause.** Capability invocations dispatch **concurrently**, up to `maxConcurrentInvocations` (32).
Earlier SDK versions processed one at a time, so a handler could assume it ran alone.

**Fix.** Synchronise any state a handler shares across invocations, the same way you would in a
concurrently called ASP.NET Core action. Each invocation gets its own DI scope, so scoped services are
already isolated.

### The UI shows a stale catalogue after something changed

**Cause.** Synchronous catalogue-shaped members (`GetInstances`, `GetProfiles`, `DeclaredVariables`,
`EventDefinitions`, …) are served from a cached `describe`, not a live call.

**Fix.** Inject `IPluginCatalogNotifier` and call `CatalogChanged(kind)` when something outside a
host-initiated invocation changes what a later `describe` would answer. It is fire-and-forget, never
throws, and is a silent no-op before a session exists.

### The supervisor reports the plugin unhealthy but it is running fine

**Cause.** Almost always an overridden listener URL. The supervisor binds a port *before* your process
starts and passes it as `ASPNETCORE_URLS`; if you override it in `appsettings.json`, a launch profile,
or `UseUrls`, you listen where nobody is probing. There is no error for this - from the supervisor's
side, a wrong port and a hung process look identical.

**Fix.** Do not set your own listener URL. [MDP4002](/reference/analyzers/#mdp4002) catches the
statically visible cases.

Failing that, check that `health.path` in the manifest matches the route you serve (default
`/_macrodeck/health`) and that `health.timeoutSeconds` is not shorter than your process needs.
Remember `unhealthyThreshold` has a floor of 2, so a single missed probe never restarts anything.

### The plugin is killed at shutdown instead of exiting cleanly

**Cause.** It did not exit within `shutdown.gracefulTimeoutSeconds` (default 10, clamped 1–60) after
`session.goodbye` and the `4004` close.

**Fix.** Make `ShutdownAsync` fast: stop accepting new work, drain what is bounded, release. Anything
logged in the final moments may not be flushed - see [logging](/features/logging/#shutdown).

### Your logs never reach the host's log viewer

Work through, in order: `UseMacroDeckLogging()` is actually called; the event's level is at or above
`MacroDeck:Plugin:Logging:MinimumLevel` (default `Information`, independent of the pipeline's own
minimum); the plugin is connected, because `log.publish` is fire-and-forget with **no replay** and a
batch lost during an outage is gone; and you are not exceeding the ingestion rate limit, which drops
excess events rather than closing the session. See [logging](/features/logging/).

## Installing an artifact

Wire codes from the installation API, and what each means:

| Code | Cause | Fix |
| --- | --- | --- |
| `invalid_archive` | Not a readable ZIP | Repack |
| `unsafe_entry` | An absolute path, a drive letter, a `..` segment, a reserved Windows device name, an illegal filename character, or a symlink/fifo/socket/device node | Remove it. See [what the installer rejects](/reference/plugin-hosting/#the-macrodeckplugin-artifact) |
| `artifact_too_large`, `artifact_limit_exceeded` | Entry count, byte totals or compression ratio over the limits | Trim the payload |
| `manifest_missing` | No `manifest.json` at the archive **root** - the root is the version directory, with no wrapper folder | Repack with `macrodeck-plugin pack` |
| `manifest_invalid` | The reader rejected the manifest | Run `macrodeck-plugin validate --artifact …` for the specifics |
| `id_mismatch` | The manifest names a different plugin than the caller asked to install | Check `id` |
| `incompatible` | `compatibility.protocol` or `compatibility.macroDeck` excludes this host. This is a rejection, not a warning | Widen the declared range, or use a matching host |
| `hash_mismatch` | A declared file digest, or the artifact's expected hash, did not match | Repack; do not edit an artifact in place |
| `signature_invalid` | The signature block is **malformed** - no `keyId`, a non-base64 `value`, or an `ed25519` value that is not 64 bytes. A signature this host merely cannot verify is not an error | Fix or remove the block |
| `already_installed` | That version is already on disk | Pass `force` to reinstall it in place. `force` does **not** bypass compatibility, digests or signature rejection |
| `health_validation_failed` | The activated version never reached a healthy running state, so activation was rolled back and the version deleted | Fix the health problem - usually the listener URL above - and reinstall |
| `dependency_in_use` | Another installed plugin hard-depends on the one being uninstalled | Remove the dependent first, or force it |
| `no_artifact` | `install` or `inspect` was called with no uploaded file | Attach the file |
| `DesktopOnly` | A local-path endpoint was called somewhere other than the desktop app. The PascalCase spelling is from the shared portability refusal, not the installer | Use the upload endpoint instead |

### It installed, but it will not start

Two cases that install and activate perfectly and still do not run:

- **An unsatisfied hard dependency, or a live conflict.** Both produce a *Blocking* warning that
  withholds the automatic start without failing the install. Install what is missing, or start it
  manually.
- **No entrypoint for this host's RID.** After the two silicon fallbacks (`osx-arm64 → osx-x64`,
  `win-arm64 → win-x64`) there is no other fallback and no `"any"` key; `linux-musl-*` resolves
  nothing. The installer adds an advisory warning and deliberately does not health-gate it, because a
  cross-platform artifact simply not built for this machine is still a valid install.

### A framework-dependent plugin reports a missing runtime

**Cause.** No `dotnet` muxer was found, or none of its installed `Microsoft.NETCore.App` runtimes
satisfy the entrypoint's `dotnetVersion`. The rule is: same major, and an installed minor at least as
high as requested - a higher major never satisfies a lower one.

**Fix.** Install the matching runtime. The plugin stays stopped rather than burning restart budget on a
launch that cannot succeed. The muxer is looked for in `DOTNET_ROOT` first, then `PATH`, then a short
list of well-known per-OS locations.

### The install is refused with a signature error

**Cause.** The host verifies a package in a staging directory before writing anything into the plugin
directory, and every verdict except "unsigned" refuses the install. `SignatureInvalid` means the
signature block is malformed, does not verify, or the contents do not match it; `SignatureUntrusted`
means the certificate does not chain to the pinned root, was issued for another purpose, or was not valid
at the signature's own `signedAt`; `SignatureRevoked` means the certificate is revoked;
`SignatureUnverifiable` means the package could not be read or its algorithm is unknown to this host.

**Fix.** Run [`macrodeck-plugin verify`](/cli/signing/#verify) against the artifact to see which
check failed, and reinstall from a good copy. A signature that is present but does not verify is a
failure rather than a weaker kind of unsigned, so no confirmation will install it.

### An unsigned plugin will not install

**Cause.** `UnsignedNotPermitted`. An unsigned package installs only on an explicit per-install
confirmation, and only for a file you selected yourself or uploaded - a store or registry install has no
way to express that consent. It is also refused, with or without consent, when the plugin id was
previously admitted as signed, because the trust tier does not move downwards.

**Fix.** Install the package from a local file and confirm the prompt, or install a signed build if the
plugin was signed before.

### A plugin that used to work now refuses to launch

**Cause.** An installed plugin is re-verified on every launch, not just at install, because the plugin
directory is user-writable. Editing files after installation, or stripping the certificate from one that
was installed as trusted, breaks that check and the refusal is terminal for the process lifetime rather
than something the restart budget retries.

**Fix.** Reinstall from a good artifact. The plugin stays installed, so a reinstall over it is enough -
see [signing](/policies/security/#signing-the-creator-portal-signs-and-the-host-verifies-before-install-and-before-every-load).

## See also

- [Plugin CLI](/cli/) - every command, option, diagnostic and exit code.
- [Plugin hosting](/reference/plugin-hosting/) - the artifact format, supervision and shutdown.
- [Authentication](/reference/authentication/) - credentials, session exchange and their errors.
- [Analyzers](/reference/analyzers/) - all 19 diagnostics with explanations.
- [Conformance](/reference/conformance/) - the contract suite, when you want a verdict rather than a
  symptom.
