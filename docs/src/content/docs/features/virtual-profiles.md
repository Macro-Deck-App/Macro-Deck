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
		=> widgetId == "go-live" && interaction.TriggerType == "onShortPress"
			? _stream.GoLiveAsync()
			: Task.CompletedTask;

	// IPluginIntegration members omitted.
}
```

The user now has a "Stream Kit" profile with a 2 x 4 grid they can't edit. Pressing "go-live" calls your
integration.

Things to know:

- **Ids are local.** You choose profile, folder and widget ids, and the host hands them back to you as
  they are. A widget id only has to be unique within its folder, so the same id can appear in every
  folder. Don't use `::` in your ids, or the profile, folder or widget is skipped.
- **Interactions are routed to you, not executed.** Virtual widgets aren't stored by the host, so a
  trigger on one reaches `HandleWidgetInteractionAsync` with your local `profileId`, `folderId` and
  `widgetId`. The default implementation does nothing.
- **Tiles draw from your data.** A virtual widget renders from its `Data` exactly as a stored widget of
  the same type and data does.

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

A virtual widget has no stored widget behind it, so anything that belongs to a stored widget does not
apply: widget-scoped variables, a slider's own default value variable (bind a slider to a variable with
`valueVariable`), and a countdown's or stopwatch's running state (they show their idle face). A label
template such as `{{ vars.volume }}` resolves global variables and follows them as they change.

### Widget ids on the wire

Clients, device providers and plugin-provided widget types see a virtual widget under an id the host
composes from the integration, profile, folder and widget ids, in the form `integrationId::localId`.
Treat that id as opaque: don't parse it or build one yourself, and pass it back unchanged where you
received it. The composed local part may be at most 256 characters, so keep profile, folder and widget
ids short; a widget whose composed id is longer is skipped and logged.

## Handling interactions

```csharp
public async Task HandleWidgetInteractionAsync(string profileId, string folderId, string widgetId,
	WidgetInteraction interaction)
{
	if (interaction.TriggerType != "onShortPress")
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

`TriggerType` uses the widget trigger names, such as `"onShortPress"`, `"onLongPress"` and
`"onDoublePress"`. Every route uses the same names: a deck tile, a plugin-provided device and the REST
API. A press or long press on a virtual Action Button, Countdown or Stopwatch, and a double press on a
virtual Slider, is routed here instead of running flows, and so is a trigger on any other widget type. A trigger for a widget id that isn't yours, or from a disabled integration, reports a
failed trigger to the client.

## Changing the profiles

`GetProfiles` is read every time the host lists profiles or opens a virtual folder, so returning a new
list is enough in process. Keep it cheap and side-effect free, and build it from state you already hold.
An out-of-process plugin must tell the host to read it again with
`CatalogChanged(CapabilityKinds.VirtualProfiles)` on an injected `IPluginCatalogNotifier`. Tiles that are
open on a deck then redraw from the new data of their widget, and a tile whose widget you removed stops
drawing. A widget you added appears the next time the deck loads the folder. An in-process provider has
no change signal, so its open tiles keep their data until the deck reloads.

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
