---
title: Features
description: Every feature a Macro Deck plugin can add, the SDK contract behind it, and where it is documented.
---

A plugin adds features by implementing small SDK interfaces on its integration. Implement only what you
need - every plugin starts with actions.

```csharp
public sealed class ObsIntegration : IPluginIntegration, IVariableProvider, IEventProvider
{
	public IReadOnlyList<IActionDefinition> Actions { get; } = [new StartRecordingAction()];
	// IVariableProvider and IEventProvider members, see their pages.
}
```

## Buttons

| Feature | Contract | Use it to |
| --- | --- | --- |
| [Actions](/features/actions/) | `IActionDefinition`, `IActionExecutor` | Do something when a button is pressed or a flow runs. |
| [Button states](/features/button-states/) | `IStateProviderActionDefinition` | Show on/off, muted, recording and similar states on a button. |
| [Button icons](/features/button-icons/) | `IIconProviderActionDefinition` | Draw album art, avatars or weather imagery on a button. |

## Data

| Feature | Contract | Use it to |
| --- | --- | --- |
| [Variables](/features/variables/) | `IVariableProvider` | Expose live values such as `{{ vars.music_track }}`. |
| [Events](/features/events/) | `IEventProvider`, `IEventPublisher` | Let users trigger automation when something happens. |
| [Music players](/features/music-players/) | `IMusicPlayerProvider` | Drive the Music Player widget and reuse Macro Deck's ready-made music actions. |
| [Weather](/features/weather/) | `IWeatherProvider` | Supply weather stations to Macro Deck's weather features. |
| [Virtual profiles](/features/virtual-profiles/) | `IProfileProvider` | Offer profiles the plugin generates. |

## Setup and maintenance

| Feature | Contract | Use it to |
| --- | --- | --- |
| [Setup flows](/features/setup-flows/) | `IConfigFlowProvider`, `IConfigFlow` | Ask for connection details, API keys or an OAuth login. |
| [Integration issues](/features/integration-issues/) | `IIntegrationIssueProvider` | Tell the user what is wrong and how to fix it. |
| [Settings migrations](/features/settings-migrations/) | `IMigrationProvider` | Take over a user's setup from Macro Deck 2. |
| [Localization](/features/localization/) | `MacroDeck.Localization` | Ship every user-facing string in every language. |
| [Logging](/features/logging/) | `ILogger` | Write diagnostics the user can send you. |
| [Testing](/features/testing/) | `MacroDeck.Plugin.Testing` | Test your integration without a running Macro Deck. |

## Hardware and surfaces

| Feature | Contract | Use it to |
| --- | --- | --- |
| [Devices](/features/devices/) | `IDeviceProvider` | Connect hardware or custom clients as Macro Deck devices. |
| [Layouts](/features/layouts/) | `ILayoutProvider` | Describe a device's regions and geometry. |
| [Macro Deck UI](/ui/) | `IUiProvider` | Draw configuration views, widgets and folder views. |
| [Widget types](/ui/views/widget-types/) | `IWidgetTypeProvider` | Add deck widgets beside Macro Deck's own. |
| [Folder views](/ui/views/folder-views/) | `IFolderViewProvider` | Replace a folder's button grid with your own rendering. |

## Host APIs

`IIntegrationContext` gives an integration access to what Macro Deck owns: `Deck` navigation, `Scripts`,
`Widgets`, `Notifications`, variables, configuration and events. In a plugin every call crosses the
plugin protocol, so don't call them in a hot loop.

## Ids

Action, event and variable ids are yours to choose and stay stable once shipped - profiles and
configuration store them.

```csharp
public sealed class StartRecordingAction : IActionDefinition
{
	public string Id => "start-recording"; // local id: lowercase kebab-case
	// ...
}
```

- **Pass the local id.** Macro Deck qualifies it with your plugin's identity; never pass an
  `owner::local` value to an API that expects a local id.
- **Runtime resource ids** that come from provider state (entities, sources, topics) may use a broader
  grammar, but must not contain `::`.

## See also

- [Capability parity](/reference/capability-parity/) - where a plugin differs from a built-in integration.
- [Compatibility](/policies/compatibility/) and [deprecations](/policies/deprecations/) - how these contracts evolve.
- [Contributing an integration](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/development/contributing-integrations.md) - built-in integrations.
