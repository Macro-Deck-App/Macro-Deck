---
title: Plugin hosting
description: Build and run an out-of-process .NET plugin with MacroDeck.Plugin.Hosting.
---

```csharp
var plugin = MacroDeckPlugin.CreatePlugin(args)
    .UseMacroDeckLogging()
    .UseLocalization(Strings.LocalizationCatalog)
    .RegisterIntegration<PluginIntegration>()
    .Build();

await plugin.RunAsync();
```

`MacroDeck.Plugin.Hosting` turns a .NET application into an out-of-process Macro Deck plugin. The project
file, manifest and `Program.cs` are on [Project setup](/introduction/manual-setup/). For an integration
compiled into Macro Deck itself, see
[Contributing an integration](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/development/contributing-integrations.md).

## What the SDK does for you

Protocol negotiation, sessions, reconnection, heartbeats, backpressure, dispatch, cancellation, host
callbacks and the reserved runtime endpoints. Plugin code implements integrations and capabilities, never
a second protocol client.

## Builder API

| Member | Does |
| --- | --- |
| `MacroDeckPlugin.CreatePlugin(args)` / `CreatePlugin()` | Starts a `PluginHostBuilder`, optionally reading configuration from the command line. |
| `RegisterIntegration<T>()` | Registers the integration as a DI singleton plus a handler per capability interface it implements. The only supported door - raw `AddSingleton` is flagged by MDP2004. |
| `RegisterIntegration<T>(Func<IServiceProvider, T>)` | Same, built by a factory. |
| `RegisterCapabilityHandler<T>()` | Adds a handler for one capability kind. |
| `UseLocalization(catalog)` | Publishes the plugin's strings and declares the `localization` capability. The catalog scope must be `plugin:<plugin-id>`. |
| `UseRegistrationMode(mode)` | Forces a [registration mode](#registration-modes) instead of inferring it. |
| `ConfigureServices((ctx, services) => ...)` | Registers services, in the order added. |
| `Configure((ctx, app) => ...)` | Adds middleware, in order, after the SDK's reserved-path middleware. |
| `UseStartup<T>()` | An `IPluginStartup` class for both halves, built once with `ActivatorUtilities`. |
| `Services`, `Configuration`, `Logging`, `Environment`, `WebApplicationBuilder` | The underlying ASP.NET Core builder. |
| `Build()` | Validates locally and returns a `PluginApplication`; throws `PluginConfigurationException` listing every problem. |
| `PluginApplication.RunAsync()` / `StartAsync()` / `StopAsync()` | Runs the plugin. Exposes `Services`, `Configuration`, `Metadata`, `Logger`, `WebApplication`. |

## Registration modes

```csharp
builder.UseRegistrationMode(PluginRegistrationMode.SelfRegistering);
```

| Mode | Started by | Credentials |
| --- | --- | --- |
| `Managed` | The Macro Deck supervisor, for an installed plugin. | Id and secret injected in the environment. The plugin never calls the registration endpoint, never enrolls and persists nothing. |
| `SelfRegistering` | A developer or tooling, on the same machine as the host. | Interactive pairing by default: the plugin requests approval, the desktop app prompts, and the approved secret is stored locally and reused. A Developer token (`EnrollmentToken`) is the headless fallback for CI and unattended setups; it skips the prompt but still needs **Developer Mode** on the host. Not for installed store artifacts. |

Left unset, the mode is inferred: an id plus a secret in configuration means managed. A managed plugin
must not override the host-provided listener; the supervisor observes it through the reserved health
endpoint. See [Authentication](/reference/authentication/).

## What the supervisor injects

A managed launch gets these environment variables. The launcher first scrubs every inherited
`MACRO_DECK_PLUGIN_*` variable and `ASPNETCORE_URLS`. Treat them as host-owned: do not replace the
listener URL or persist credentials for later runs.

| Variable | Option (`MacroDeck:Plugin:*`) | Value |
| --- | --- | --- |
| `MACRO_DECK_PLUGIN_MODE` | `Mode` | `Managed` |
| `MACRO_DECK_PLUGIN_HOST_URL` | `HostUrl` | `http://127.0.0.1:<host port>` |
| `MACRO_DECK_PLUGIN_ID` | `Id` | Plugin id |
| `MACRO_DECK_PLUGIN_SECRET` | `Secret` | Plugin secret |
| `MACRO_DECK_PLUGIN_DATA_DIRECTORY` | `DataDirectory` | Plugin state directory that survives updates and rollbacks |
| `MACRO_DECK_PLUGIN_INSTANCE_ID` | `InstanceId` | New id per launch |
| `MACRO_DECK_PLUGIN_LAUNCH_ID` | `LaunchId` | Launch id, for diagnostics and log correlation only |
| `MACRO_DECK_PLUGIN_HOST_PROCESS_ID` | `HostProcessId` | Host pid |
| `MACRO_DECK_PLUGIN_HOST_STARTED_AT` | `HostStartedAt` | Host start time, UTC round-trip format |
| `ASPNETCORE_URLS` | - | `http://127.0.0.1:<health port>` |

The SDK also reads, but the supervisor does not set: `MACRO_DECK_PLUGIN_ENROLLMENT_TOKEN`
(`EnrollmentToken`), `MACRO_DECK_PLUGIN_STATE_DIRECTORY` (`StateDirectory`, where a self-registering plugin
keeps its credentials), `MACRO_DECK_PLUGIN_PAIRING` (`PairingEnabled`, default `true`) and
`MACRO_DECK_PLUGIN_PAIRING_TIMEOUT` (`PairingTimeout`). `HostUrl` defaults to `http://127.0.0.1:8193`. All
of `PluginHostOptions` binds from `MacroDeck:Plugin`, so `appsettings.json` and the command line work too.

## Lifecycle

```
connect -> session established -> InitializeAsync
  resume            -> integrations keep running
  session lost      -> ShutdownAsync -> new session -> InitializeAsync
shutdown requested  -> ShutdownAsync -> process exits
```

- Integrations initialise only after a session exists, because `IIntegrationContext` host APIs need a live
  connection.
- `InitializeAsync` and `ShutdownAsync` may run more than once per process; make them safe across session
  replacement.
- Capability invocations run concurrently. Protect mutable shared state as in any concurrently used ASP.NET
  Core service.

## What to expect at shutdown

1. Macro Deck requests graceful shutdown over the protocol.
2. The plugin honours cancellation; hosted services and integrations stop.
3. If it has not exited within the grace period (`shutdown.gracefulTimeoutSeconds`), the supervisor kills
   the process.

### A managed plugin exits when its host does

```csharp
builder.Services.Configure<PluginHostOptions>(options =>
{
    options.ExitWhenHostProcessDies = false;
});
```

If the host crashes or is force-killed, no shutdown request is sent and a dropped connection looks the same
as a restart. So a managed plugin watches the host process that launched it and stops itself, as a normal
application stop (hosted services and `ShutdownAsync` run), within a few seconds - the check runs about
every five seconds.

| `ExitWhenHostProcessDies` | Managed | Self-registering |
| --- | --- | --- |
| unset (default) | Watches | Keeps running |
| `true` | Watches | Watches |
| `false` | Keeps running (useful to keep a debugger attached across a host restart) | Keeps running |

The host is identified by pid **and** start time, since pids are reused. If a host does not supply both, as
an older Macro Deck or `macrodeck plugin run` does not, the watch is disabled.

## `IIntegrationContext`

Host capabilities: variables, configuration, deck navigation, scripts, widgets, events, notifications and
action interactions. Out of process these are protocol calls or cached protocol state, so prefer cached or
event-driven designs to tight loops over host APIs. See [Capabilities](/features/) and
[Capability parity](/reference/capability-parity/).

## Reserved routes

| Route | Answers |
| --- | --- |
| `/_macrodeck/health` | Liveness - as soon as the process serves, regardless of session. |
| `/_macrodeck/ready` | Readiness - only once a session is open. |
| `/_macrodeck/info` | The plugin's metadata and mode. |
| `/_macrodeck/diagnostics` | Connection state, queue depths, capability counts. |

All of `/_macrodeck/*` is reserved (`/_macrodeckery` is not). Do not map routes there: middleware added
with `Configure` runs after the SDK's and cannot answer them, and a constant path there is analyzer error
MDP2005. The plugin's own listener defaults to loopback; exposing custom endpoints to the network is a
separate, explicit security decision.

## Errors and connection loss

| Option | Default | Effect |
| --- | --- | --- |
| `FailFastOnFirstConnect` | `false` | Stop instead of retrying when the host is unreachable at startup. |
| `StopApplicationOnFatalProtocolError` | on when managed, off when self-registering | Stop on an unsupported version, a session replaced by another instance, or repeated authentication failures. |
| `MaxAuthenticationFailures` | `3` (1-100) | Consecutive failures before giving up. A stored secret is never discarded automatically. |

Report domain and provider failures through the SDK result types and honour cancellation tokens. Do not
wrap the hosting runtime in a second connection loop.

## Logging

Use normal application logging; `MacroDeck.Plugin.Serilog` forwards Serilog into Macro Deck's log
pipeline. See [Logging](/features/logging/). Never log credentials, OAuth tokens, authorization headers or
other reusable secrets.

## Signing

Signing belongs to packaging and publishing, not the hosting API. Store artifacts are signed server-side by
the Creator Portal; no signing key reaches a plugin author or CI - see
[Publishing to the Store](/guides/publishing/). Check an artifact with [`verify`](/cli/signing/#verify); the
trust model and what the host still does not enforce are in the [security model](/policies/security/).

## The .macroDeckPlugin artifact

A ZIP containing `manifest.json` and the declared platform payload. A development process can run straight
from its build output; installed plugins come as artifacts - create and validate them with
[the plugin CLI](/cli/). Installation and activation policy belongs to the packaging contract and the
installer, not the hosting runtime.

The installer enforces only the Development
[manifest requirement level](/reference/manifest/#requirement-categories). Package and Publication are
tooling-side (`build`, `pack`, `validate`, the Creator Portal), so missing publishing metadata never blocks a
local install.

## Testing

Use `MacroDeck.Plugin.Testing` - see [Testing plugins](/features/testing/) and the
[Conformance suite](/reference/conformance/). Test observable capability behaviour and lifecycle
assumptions, not protocol internals the hosting package owns.

## Related reference

- [SDK reference](/reference/sdk-packages/)
- [Plugin protocol](/reference/protocol/)
- [Manifest](/reference/manifest/)
- [Capabilities](/features/)
- [Compatibility policy](/policies/compatibility/)
