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
| [Messaging between plugins](/features/messaging/) | `IIntegrationContext.Messages` | Talk to other plugins and integrations by topic: events, commands and requests. |
| [Deck and clients](/features/deck/) | `IIntegrationContext.Deck` | Navigate folders and profiles, and find out which folder each client has open. |
| [Music players](/features/music-players/) | `IMusicPlayerProvider`, `IIntegrationContext.MusicPlayers` | Drive the Music Player widget and reuse Macro Deck's ready-made music actions, and read every provider's players and album colours. |
| [Weather](/features/weather/) | `IWeatherProvider` | Supply weather stations to Macro Deck's weather features. |
| [Calendars](/features/calendars/) | `ICalendarProvider` | Supply calendar accounts and events to Macro Deck's calendar widgets, triggers and Join Meeting action. |
| [Email inboxes](/features/email-inboxes/) | `IEmailInboxProvider` | Supply email inboxes to Macro Deck's email widget, variables and triggers. |
| [Virtual profiles](/features/virtual-profiles/) | `IProfileProvider` | Offer profiles the plugin generates. |

## Setup and maintenance

| Feature | Contract | Use it to |
| --- | --- | --- |
| [Setup flows](/features/setup-flows/) | `IConfigFlowProvider`, `IConfigFlow` | Ask for connection details, API keys or an OAuth login. |
| [Integration issues](/features/integration-issues/) | `IIntegrationIssueProvider` | Tell the user what is wrong and how to fix it. |
| [Notifications](/features/notifications/) | `INotificationService` | Show a toast or a dialog on every client and get the user's answer. |
| [Settings migrations](/features/settings-migrations/) | `IMigrationProvider` | Take over a user's setup from Macro Deck 2. |
| [Integration storage](/features/integration-storage/) | `IIntegrationContext.Storage` | Keep small documents of your own, such as devices you have seen, across restarts. |
| [Localization](/features/localization/) | `MacroDeck.Localization` | Ship every user-facing string in every language. |
| [Logging](/features/logging/) | `ILogger` | Write diagnostics the user can send you. |
| [Testing](/features/testing/) | `MacroDeck.Plugin.Testing` | Test your integration without a running Macro Deck. |

## Hardware and surfaces

| Feature | Contract | Use it to |
| --- | --- | --- |
| [Devices](/features/devices/) | `IDeviceProvider` | Connect hardware or custom clients as Macro Deck devices. |
| [Layouts](/features/layouts/) | `ILayoutProvider` | Describe a device's regions and geometry. |
| [Android devices](/features/android-devices/) | `IAndroidDeviceManager` | Run shell commands, copy files and install apps on Android devices through Macro Deck's ADB connection. |
| [Batteries](/features/batteries/) | `IBatteryProvider`, `IIntegrationContext.Batteries` | Report the battery levels of phones, headphones, controllers and other devices, and read every provider's devices. |
| [Companion devices](/features/companion-devices/) | `ICompanionDeviceApi` | Read what the phones and tablets connected through the Companion app report, send them commands and take screenshots. |
| [Macro Deck UI](/ui/) | `IUiProvider` | Draw configuration views, widgets and folder views. |
| [Widget types](/ui/views/widget-types/) | `IWidgetTypeProvider` | Add deck widgets beside Macro Deck's own. |
| [Folder views](/ui/views/folder-views/) | `IFolderViewProvider` | Replace a folder's button grid with your own rendering. |
| [Screensavers](/ui/views/screensavers/) | `IScreenSaverProvider` | Show something of your own on a device that has sat idle. |
| [Video streams](/features/video-streams/) | `IVideoStreamIntegration`, `IVideoStreamProvider` | Offer live video, such as cameras or OBS scenes, for Macro Deck to show. |

## Host APIs

`IIntegrationContext` gives an integration access to what Macro Deck owns: [`Deck`](/features/deck/) navigation and client positions, `Scripts`,
`Widgets`, [`NotificationService`](/features/notifications/), `Notifications`, variables, configuration, events, [messaging](/features/messaging/), [battery devices](/features/batteries/#reading-devices), [music players](/features/music-players/#reading-every-providers-players), [images for your own UI](/ui/reference/resources/#registering-your-own-images), [storage](/features/integration-storage/), the user's [time format](/features/localization/#formatting-a-time-of-day) and the [User-Agent](/reference/plugin-hosting/#outgoing-http-requests) for outgoing requests. In a plugin every call crosses the
plugin protocol, so don't call them in a hot loop. [Android devices](/features/android-devices/) and
[Companion devices](/features/companion-devices/) can also be taken from dependency injection, as
`IAndroidDeviceManager` and `ICompanionDeviceApi`.

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
