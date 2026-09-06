---
title: Plugin hosting
description: Build and run an out-of-process .NET plugin with MacroDeck.Plugin.Hosting.
---

`MacroDeck.Plugin.Hosting` turns a .NET application into an out-of-process Macro Deck plugin. It handles the plugin protocol, connection lifecycle, dependency injection, capability dispatch, and host callbacks so plugin code can focus on integrations.

For an integration compiled directly into Macro Deck - a contribution to the Macro Deck repository rather than a plugin - see [Contributing an integration](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/development/contributing-integrations.md).

## What the SDK does for you

The hosting package owns protocol negotiation, sessions, reconnection, heartbeats, dispatch, cancellation, host callbacks, and the reserved runtime endpoints. Plugin code implements integrations and capabilities rather than a second protocol client.

## Create the project

A normal .NET console project is enough:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
    <PackageReference Include="MacroDeck.Plugin.Hosting" Version="3.0.0" />
  </ItemGroup>
</Project>
```

The hosting package builds on ASP.NET Core. You can use normal DI, configuration, logging, `HttpClientFactory`, hosted services, and middleware.

## Add the manifest

Plugin identity comes from `manifest.json`, not from code:

```json
{
  "$schema": "https://schemas.macro-deck.app/plugin-manifest-v1.schema.json",
  "manifestVersion": 1,
  "id": "com.example.my-plugin",
  "name": "My Plugin",
  "version": "1.0.0",
  "description": "What the plugin does.",
  "icon": "assets/icon.svg",
  "entrypoints": {
    "win-x64": { "executable": "MyPlugin.exe" },
    "osx-arm64": { "executable": "MyPlugin" },
    "linux-x64": { "executable": "MyPlugin" }
  }
}
```

Copy the manifest and referenced assets to the output directory. See the [manifest reference](/reference/manifest/) for all fields and the [plugin CLI](/cli/) for the final `.macroDeckPlugin` artifact.

## Start the plugin

```csharp
using MacroDeck.Plugin.Hosting;

var plugin = MacroDeckPlugin.CreatePlugin(args)
    .RegisterIntegration<MyIntegration>()
    .Build();

await plugin.RunAsync();
```

`RegisterIntegration<T>()` registers the integration and the capability interfaces it implements. `Build()` validates local configuration before opening a session, including manifest identity, duplicate capability ids, reserved routes, and the DI graph.

## Configure services

The builder exposes the normal ASP.NET Core surfaces for services, configuration, logging, environment, and middleware. Use them when your plugin needs its own dependencies or hosted services.

The underlying `WebApplicationBuilder` and built `WebApplication` remain available as escape hatches. Prefer the Macro Deck APIs when they cover the use case, especially for registration, lifecycle, and protocol behavior.

## Registration modes

Plugins run in one of two modes.

### Managed

An installed plugin is launched and supervised by Macro Deck. The host supplies launch credentials and listener configuration. The plugin does not enroll itself or persist a host credential.

Managed plugins should not override the host-provided listener configuration. The supervisor uses the plugin's reserved health endpoint to observe the process.

### Self-registering / development

A plugin started by a developer enrolls through interactive pairing by default: it requests approval, the desktop app shows a prompt, and the plugin stores the resulting plugin registration secret locally on approval. A Developer token remains available as the headless/automation fallback for CI runners and other unattended setups with no one to approve a prompt - it removes the approval step, not the requirement that **Developer Mode** be enabled on the host. This mode is intended for development and tooling, not for installed store artifacts.

See [Authentication](/sdk/authentication/) for the credential flow.

## What the supervisor injects

A managed launch receives the listener/session bootstrap information required by the hosting runtime. Treat those values as host-owned launch configuration. Do not replace the listener URL or persist launch credentials for later runs.

## Lifecycle

Integrations initialize after a Macro Deck session is established, because `IIntegrationContext` host APIs require a live connection.

A resumed session continues the existing integration lifecycle. A fresh session after the previous one is lost can cause integrations to shut down and initialize again. `InitializeAsync` and `ShutdownAsync` should therefore be safe to run across session replacement rather than assuming exactly one initialization per process lifetime.

Capability invocations can run concurrently. Protect mutable shared plugin state just as you would in a concurrently used ASP.NET Core service.

## What to expect at shutdown

For a managed plugin, Macro Deck first requests graceful shutdown through the protocol. The plugin should honor cancellation and allow hosted services/integrations to stop cleanly. The supervisor may terminate the process if it does not exit within the configured grace period.

### A managed plugin exits when its host does

Graceful shutdown only happens when Macro Deck is asked to stop and given time to finish. If the host process is killed instead - it crashes, or it is force-terminated after failing to stop in time - no shutdown request is ever sent. A plugin cannot tell that apart from a dropped connection, so it would reconnect indefinitely and keep running in the background long after Macro Deck is gone.

A managed plugin therefore watches the host process that launched it, and stops itself once that process is gone. The check runs about every five seconds, so expect a managed plugin to exit within a few seconds of an unexpected host death rather than instantly. It is a normal application stop: hosted services and `ShutdownAsync` run as usual.

**Self-registering plugins are not affected.** A plugin you started yourself is an independent process that Macro Deck did not launch, and it keeps running when the host goes away - which is what you want while developing against a host you restart repeatedly.

To override the default in either direction, set `ExitWhenHostProcessDies`:

```csharp
builder.Services.Configure<PluginHostOptions>(options =>
{
    options.ExitWhenHostProcessDies = false;
});
```

`false` keeps a managed plugin alive after its host dies, which is occasionally useful when attaching a debugger to a plugin across a host restart. `true` opts a self-registering plugin into the same watch. Leaving it unset - the default - watches only in managed mode.

The host identifies itself to the plugin by both process id and process start time. Process ids are reused, so a plugin that only knew the id could exit because an unrelated process happened to inherit it. If a host does not supply that information, as an older Macro Deck or `macrodeck plugin run` does not, the watch is simply disabled and the plugin runs exactly as before.

## `IIntegrationContext`

The integration context provides host capabilities such as variables, configuration, deck navigation, scripts, widgets, events, notifications, and action interactions.

For an out-of-process plugin these are protocol calls or cached protocol state rather than direct in-process references. Avoid high-frequency loops that repeatedly call host APIs when a cached or event-driven design is possible.

See [Capabilities](/sdk/capabilities/) and [Capability parity](/sdk/capability-parity/) for behavior that differs from an in-process integration.

## Reserved routes

`/_macrodeck/*` is reserved by the hosting runtime for health and runtime endpoints. Do not map plugin routes under that prefix.

A plugin's own application listener defaults to loopback. Publishing custom plugin HTTP endpoints to the network is separate from the Macro Deck plugin protocol and should be an explicit security decision.

## Errors and connection loss

The hosting runtime owns protocol reconnection, heartbeats, backpressure, and session framing. Plugin capability code should report domain/provider failures through the relevant SDK result types and honor cancellation tokens.

Do not implement a second connection loop around the hosting runtime.

## Logging

Use normal application logging for plugin diagnostics. `MacroDeck.Plugin.Serilog` can forward Serilog events into Macro Deck's log pipeline. See [Logging](/sdk/logging/).

Never log plugin credentials, OAuth tokens, authorization headers, or other reusable secrets.

## Signing

Signing belongs to the packaged artifact/publishing workflow, not to the runtime hosting API. Artifacts
published to the Store are signed by the Creator Portal, server-side, and no signing key ever reaches a
plugin author or a CI workflow - see [Publishing to the Store](/guides/publishing/). Check a signed
artifact with the CLI's [`verify`](/cli/signing/#verify) command, and see the
[security model](/policies/security/) for the trust model behind it and what host-side enforcement still
does not do.

## The .macroDeckPlugin artifact

Installed plugins are distributed as `.macroDeckPlugin` ZIP artifacts containing `manifest.json` and the declared platform payload. The hosting runtime itself does not define installation/activation policy; the shared packaging contract and Macro Deck installer do.

The installer enforces only the Development [manifest requirement level](/reference/manifest/#requirement-categories) - the Package and Publication levels are tooling-side (`macrodeck-plugin build`/`pack`/`validate` and the Creator Portal) and are never applied at install time, so publishing metadata a plugin has not filled in yet never blocks a local install.

## Testing

Use `MacroDeck.Plugin.Testing` for host-facing tests and the conformance suite:

- [Testing plugins](/sdk/testing/)
- [Conformance suite](/sdk/conformance/)

Test observable capability behavior and lifecycle assumptions rather than protocol internals already owned by the hosting package.

## Packaging

A development process can run directly from its build output. Installed plugins are distributed as `.macroDeckPlugin` artifacts with the manifest and platform entrypoints.

See [the plugin CLI](/cli/) for creating and validating the artifact.

## Related reference

- [SDK reference](/sdk/)
- [Plugin protocol](/reference/protocol/)
- [Manifest](/reference/manifest/)
- [Capabilities](/sdk/capabilities/)
- [Compatibility policy](/policies/compatibility/)
