---
title: Virtual profiles
description: Ship ready-made, read-only profiles with IProfileProvider - fixed layouts, folders, widgets and routed widget interactions.
---

:::note
Out-of-process plugins get **partial** support: profile catalogs and widget interactions work, but the
catalog is snapshot-backed and interactions are fire-and-forget. See
[Capability parity](/reference/capability-parity/).
:::

An integration ships ready-made profiles by implementing `IProfileProvider`. Macro Deck shows them next
to the user's own profiles, read-only and with a fixed layout, and it creates no profile file for them.

## Quick start

```csharp
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Profiles;

public sealed class StreamKitIntegration : IPluginIntegration, IProfileProvider
{
	private readonly StreamClient _stream = new();

	public IReadOnlyList<IActionDefinition> Actions { get; } = [];

	public IReadOnlyList<VirtualProfileDescriptor> GetProfiles() =>
	[
		new VirtualProfileDescriptor("stream-kit", "Stream Kit", ProfileLayout.Grid(rows: 2, columns: 4),
		[
			new VirtualFolderDescriptor("main", "Main",
			[
				new VirtualWidgetDescriptor("go-live", "ActionButton", PositionX: 0, PositionY: 0),
				new VirtualWidgetDescriptor("now-playing", "MusicPlayer", PositionX: 1, PositionY: 0, Width: 2)
			])
		])
	];

	public Task HandleWidgetInteractionAsync(string profileId, string folderId, string widgetId,
		WidgetInteraction interaction)
		=> widgetId == "go-live" && interaction.TriggerType == "press"
			? _stream.GoLiveAsync()
			: Task.CompletedTask;

	// IPluginIntegration members omitted.
}
```

The user now has a "Stream Kit" profile with a 2 x 4 grid they can't edit. Pressing "go-live" calls your
integration.

Things to know:

- **Ids are local.** The host prefixes profile, folder and widget ids as `integrationId::localId`. Don't
  use `::` in your ids, or the profile, folder or widget is skipped.
- **Interactions are routed to you, not executed.** Virtual widgets aren't stored by the host, so a
  trigger on one reaches `HandleWidgetInteractionAsync`. The default implementation does nothing.
- **Don't rely on `profileId`.** The host currently passes an empty string. Identify the widget by
  `folderId` and `widgetId`, which arrive as your local ids.

## Describing a profile

| Type | Members |
| --- | --- |
| `VirtualProfileDescriptor` | `Id`, `Name`, `Layout`, `Folders` |
| `ProfileLayout` | `Kind`, `Rows`, `Columns`, `RowsLocked`, `ColumnsLocked`. Create one with `ProfileLayout.Grid(rows, columns, locked = true)`. |
| `VirtualFolderDescriptor` | `Id`, `Name`, `Widgets`, `ParentId = null`, `Order = 0` |
| `VirtualWidgetDescriptor` | `Id`, `Type`, `PositionX`, `PositionY`, `Width = 1`, `Height = 1`, `Data = null` |

`LayoutKind.Grid` is the only layout today. Every folder uses the profile's rows and columns, and
`RowsLocked`/`ColumnsLocked` switch off the matching grid controls in the editor. Nest folders with
`ParentId`, and sort them with `Order`.

`Type` is a widget type name such as `"ActionButton"`, `"MusicPlayer"` or `"Slider"`. `Data` is the same
JSON payload a stored widget of that type carries. A type whose provider hasn't connected yet is passed
through unchanged rather than becoming a button, so it appears once that provider is available.

## Handling interactions

```csharp
public async Task HandleWidgetInteractionAsync(string profileId, string folderId, string widgetId,
	WidgetInteraction interaction)
{
	if (interaction.TriggerType != "press")
	{
		return;
	}

	switch ((folderId, widgetId))
	{
		case ("main", "go-live"):
			await _stream.GoLiveAsync();
			break;
		case ("main", "end"):
			await _stream.EndAsync();
			break;
	}
}
```

`TriggerType` uses the action button's trigger names, such as `"press"` and `"release"`. The host routes
action button triggers on a virtual widget here. A trigger for a widget id that isn't yours, or from a
disabled integration, reports a failed trigger to the client.

## Changing the profiles

`GetProfiles` is read every time the host lists profiles or opens a virtual folder, so returning a new
list is enough in process. Keep it cheap and side-effect free, and build it from state you already hold.
An out-of-process plugin must tell the host to read it again with
`CatalogChanged(CapabilityKinds.VirtualProfiles)` on an injected `IPluginCatalogNotifier`.

## Edge cases

- **A disabled integration's profiles disappear** from the list, along with their folders.
- **A profile id that `GetProfiles` no longer returns** opens with no folders.
- **Read-only means read-only.** The user can't move, add or edit widgets in a virtual profile. Offer
  configuration through your integration instead.
- **`ProviderName`** is optional. Leave it out and the integration's name (the manifest name for a plugin)
  is used.

## Over the plugin protocol

Capability kind `virtual-profiles`, with the `profiles` and `widget-interaction` operations. The profile
list is served from the last snapshot the plugin sent (see
[Snapshot-backed state](/reference/capability-parity/#snapshot-backed-state)). An interaction is
delivered fire-and-forget: the client is told the trigger succeeded once it is routed, whatever your
handler does. See [the WebSocket reference](/reference/websocket/#capabilities).

## See also

- [Music players](/features/music-players/) - the `MusicPlayer` widget a profile can include.
- [Actions](/features/actions/)
- [Capability parity](/reference/capability-parity/)
- [Testing](/features/testing/)
