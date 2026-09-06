# MacroDeck.Plugin.Hosting

Write a [Macro Deck](https://github.com/Macro-Deck-App/Macro-Deck) plugin the way you would write an
ASP.NET Core application. Registration, protocol framing, heartbeats, backpressure and reconnection are
handled for you; you write integrations and let dependency injection build them.

A plugin declares what it is in a `manifest.json`, copied to the output directory so it sits at the
plugin's content root:

```json
{
  "manifestVersion": 1,
  "id": "com.example.my-plugin",
  "name": "My Plugin",
  "version": "1.0.0",
  "icon": "assets/icon.svg",
  "entrypoints": { "win-x64": { "executable": "MyPlugin.exe" } }
}
```

```csharp
var plugin = MacroDeckPlugin.CreatePlugin(args)
    .RegisterIntegration<MyIntegration>()
    .Build();

await plugin.RunAsync();
```

You get the full generic host: `IServiceCollection`, `IConfiguration`, options binding,
`IHttpClientFactory`, `IHostedService` and `BackgroundService`, middleware through `IApplicationBuilder`,
an optional `IPluginStartup` class, and graceful shutdown.

Your plugin project needs the ASP.NET Core shared framework - either `<Project Sdk="Microsoft.NET.Sdk.Web">`
or a console project with `<FrameworkReference Include="Microsoft.AspNetCore.App" />`.

Routes under `/_macrodeck/*` are reserved by the SDK for health, readiness and diagnostics; mapping one
yourself fails at startup rather than silently at request time.

Every capability kind an in-process integration can implement - actions, events, variables, icons,
config flow, music players, weather, virtual profiles and issues - has a working remote adapter, and
`IIntegrationContext` is a real, working call back into the host. **Status:** the host endpoints a
plugin connects to are not in a released Macro Deck yet
([#411](https://github.com/Macro-Deck-App/Macro-Deck/issues/411)).

Three behaviors changed recently, worth knowing if you built against an earlier version:

- **Capability invocations now dispatch concurrently**, up to `ProtocolLimits.MaxConcurrentInvocations`
  at a time, rather than one at a time. A handler that shares mutable state without its own
  synchronization can now genuinely run concurrently with itself.
- **Integration initialization no longer runs at process start.** It is gated on the connection
  actually being established: a resumed session is a no-op, and a non-resume reconnect shuts every
  integration down and re-initializes them, since cached host-side state from the old session cannot
  be trusted. The host telling a plugin its config changed now triggers the same re-initialize cycle,
  so a config flow's result reaches a running plugin without waiting for a reconnect.
- **A plugin can now invalidate the host's cached snapshot itself.** Inject `IPluginCatalogNotifier` and
  call `CatalogChanged(kind)` after anything that changes what a later `describe` would answer - a
  config value your own `InitializeAsync` just read, a device that came or went. Fire-and-forget, like
  `IEventPublisher.Publish`: never throws, and a no-op with no open session.

See [the plugin hosting guide](https://docs.macro-deck.app/sdk/hosting/)
for the full detail on both, and [the capability parity matrix](https://docs.macro-deck.app/sdk/capability-parity/)
for every documented difference from an in-process integration.

Licensed under Apache-2.0.
