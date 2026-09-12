---
title: Layout providers
description: Describe a device's surface with ILayoutProvider - regions, grid geometry and rendering ability - and point devices at it.
---

A layout provider describes what a device or client surface **is** - its regions, their geometry and what
they can render - never what is on it. Macro Deck uses a registered layout to constrain a profile to real
hardware, so hardware support lives entirely in your plugin with no device-specific code in Macro Deck.

## Quick start

```csharp
using MacroDeck.Sdk;
using MacroDeck.Sdk.Devices;
using MacroDeck.Sdk.Layouts;

public sealed class MacroPadIntegration : IPluginIntegration, ILayoutProvider, IDeviceProvider
{
	private string? _layoutId;

	public string ProviderName => "Macro Pad";

	public async Task InitializeAsync(ILayoutProviderContext context, CancellationToken cancellationToken = default)
	{
		var registration = await context.RegisterLayoutAsync(
			new LayoutDescriptor(
				"pad-3x2",
				"Macro Pad",
				Regions:
				[
					new LayoutRegion
					{
						Id = "keys",
						Kind = LayoutRegionKinds.Grid,
						Grid = new LayoutGrid { Rows = 2, Columns = 3, KeySize = new LayoutKeySize(72, 72) }
					}
				],
				Capabilities: new LayoutCapabilities
				{
					Visuals = new LayoutVisualCapabilities
					{
						StaticIcons = true, BackgroundColors = true, TextLabels = true
					}
				}),
			cancellationToken);

		_layoutId = registration.LayoutId;
	}

	public async Task InitializeAsync(IDeviceProviderContext context, CancellationToken cancellationToken = default)
	{
		await context.RegisterDeviceAsync(
			new DeviceDescriptor("SERIAL-1", "Macro Pad", LayoutReference: _layoutId),
			cancellationToken);
	}

	public Task ShutdownAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

	// IPluginIntegration members omitted.
}
```

A profile that is the startup profile of this pad is now locked to 2x3 in the editor, and its spacing and
corner-radius controls are disabled because the layout does not claim them.

Things to know:

- **Register the layout, then use the id it returns.** `LayoutRegistration.LayoutId` is the qualified
  `your.plugin.id::pad-3x2`; that is what `DeviceDescriptor.LayoutReference` must carry.
- **Layouts start before devices.** In a plugin the host initializes `ILayoutProvider` before
  `IDeviceProvider`, both after the integration's own `InitializeAsync`, so `_layoutId` is set in time.
- **There is no `ILayoutProvider.ShutdownAsync`.** Release what you acquired in the integration's own
  `ShutdownAsync`; the host withdraws your layouts when the integration stops.
- **Every visual flag defaults to `false`.** Declare what the surface can actually render.

## Registering a layout

| `ILayoutProviderContext` member | What it does |
| --- | --- |
| `RegisterLayoutAsync` | Registers a layout, or replaces the one under the same provider-local id. Devices referencing it pick up the new descriptor without re-registering. Returns `LayoutRegistration(LayoutId, ProviderId)`. |
| `UnregisterLayoutAsync` | Withdraws a layout by provider-local id. Unknown ids are ignored. |

`RegisterLayoutAsync` throws `ArgumentException` when the id or name is empty, or a region id is empty or
repeated within the layout. The context is safe to keep while the provider runs.

Implement `GetLayouts()` to return what you currently offer; the host reads it to recover its view after a
reconnect. The default returns an empty list. `ProviderName` is optional and falls back to the
integration's name - for a plugin, the `manifest.json` name.

## Id grammar

```text
you register     LayoutDescriptor.Id          "pad-3x2"
host returns     LayoutRegistration.LayoutId  "com.example.macropad::pad-3x2"
device carries   LayoutReference              "com.example.macropad::pad-3x2"
```

`LayoutDescriptor.Id` is provider-local, stable across restarts and unique within your provider - a model
name, not a per-device serial. Never build the qualified string yourself; read it back from the
registration. The prefix is always the authenticated provider's own id, so no plugin can register a layout
into another owner's namespace.

## Declaring regions

```text
+----+----+----+----+
| k  | k  | k  | k  |   "keys"    grid 2x4
+----+----+----+----+
| k  | k  | k  | k  |
+----+----+----+----+
|    touch strip    |   "strip"   touch-strip
+-------------------+
 (o)  (o)  (o)  (o)     "dials"   encoder x4
```

```csharp
Regions:
[
	new LayoutRegion
	{
		Id = "keys", Kind = LayoutRegionKinds.Grid,
		Grid = new LayoutGrid { Rows = 2, Columns = 4, KeySize = new LayoutKeySize(120, 120) }
	},
	new LayoutRegion { Id = "strip", Kind = LayoutRegionKinds.TouchStrip, Count = 1 },
	new LayoutRegion { Id = "dials", Kind = LayoutRegionKinds.Encoder, Count = 4, Name = "Dials" }
]
```

`Kind` is one of `LayoutRegionKinds` - `grid`, `button`, `encoder`, `touch-strip`, `pedal` - but it is an
**open string**: an unknown kind round-trips intact with its `Id`, `Name`, `Count` and `Extra`, so an older
host carries it without dropping the rest of the layout.

Only `grid` carries geometry, in `LayoutRegion.Grid`. Every other kind is a `Count`.

| `LayoutGrid` member | Meaning |
| --- | --- |
| `Rows`, `Columns` | The size right now. On a fixed grid, Macro Deck holds a profile edited for this layout to exactly these. |
| `IsConfigurable` | The user may pick another size. `false` (default) ignores the bounds below. |
| `MinRows`, `MaxRows`, `MinColumns`, `MaxColumns` | Per-axis bounds for a configurable grid. Default 1. |
| `SupportsRuntimeResize` | A resize applies live. When `false`, you may apply it when the device next connects. |
| `KeySize` | Pixel size of one key, where the hardware has a fixed one. |
| `RowsLocked`, `ColumnsLocked` | Derived: fixed, or configurable with min >= max on that axis. |

`LayoutDescriptor.PrimaryGrid` is the one grid Macro Deck renders a deck onto, computed for you. It is null
when the layout has no grid region (a pedal board) or more than one - Macro Deck does not guess.

## Declaring what the surface renders

```csharp
Capabilities: new LayoutCapabilities
{
	Visuals = new LayoutVisualCapabilities
	{
		StaticIcons = true, TextLabels = true, BackgroundColors = true, MaxUpdatesPerSecond = 10
	}
}
```

| `LayoutVisualCapabilities` | Meaning |
| --- | --- |
| `StaticIcons`, `AnimatedIcons`, `Borders`, `BackgroundColors`, `TextLabels`, `Transparency` | What the surface can draw. |
| `WidgetSpacing`, `CornerRadius` | Whether the folder's spacing and corner radius mean anything here. `false` disables those editor controls with a note that they have no effect on this device. |
| `CustomFolderViews` | Whether the surface can show a [folder view](/ui/views/folder-views/) at all. `false` means the choice is not offered for a profile its device claims. Set it on a software client or full display. |
| `MaxUpdatesPerSecond` | Advisory update rate. |

`LayoutVisualCapabilities.Full` sets every flag. A `Visuals` block that sets nothing reads as "renders
nothing". **No `Visuals` block at all** keeps the spacing controls and the folder-view option - saying
nothing is not saying no. A region can override with its own `LayoutRegion.Visuals`; null inherits the
layout's.

## Associating a layout with a device provider

```csharp
new DeviceDescriptor(serial, "Macro Pad", LayoutReference: "com.other.plugin::pad-3x2")
```

A device points at a layout through `DeviceDescriptor.LayoutReference` - see
[device providers](/features/devices/#capabilities-and-layouts). The layout's provider need not be the
device's: a device may reference another plugin's layout. A layout is descriptive metadata, and referencing
one grants nothing over its owner. An unresolvable reference is never an error - the device registers, and
its profile stays unconstrained until that layout appears.

## What happens to profiles

| Startup-profile devices resolve to | Profile |
| --- | --- |
| Exactly one distinct rows x columns pair (one device, or several sharing a layout) | Locked to that grid. |
| Different pairs | Editable, with a warning naming the disagreeing devices. |
| A layout with no `PrimaryGrid` | Not constrained. |

Macro Deck constrains a profile; it never rewrites one. No reflow, pagination, resizing or dropped widgets:
a profile bigger than the device simply shows partially, as a [device session](/features/devices/#receiving-and-rendering-a-session)
does.

The host persists the last resolved layout with the device, not just the reference. `UnregisterLayoutAsync`
and a stopped provider behave the same: the reference is kept, the grid stays constrained, and it refreshes
when the layout is registered again.

## In a plugin

Declare the `layout-provider` capability and `host:layouts` in [`manifest.json`](/reference/manifest/).
`MacroDeck.Plugin.Hosting` starts the provider once the plugin is connected and its integration has
initialized, and stops it on shutdown. The provider is always the authenticated plugin, which keeps the id
grammar true across the wire. See [Capability parity](/reference/capability-parity/).

## Testing

```csharp
using MacroDeck.Plugin.Testing.Fakes;
using MacroDeck.Sdk.Layouts;

[Test]
public async Task The_pad_declares_one_fixed_2x3_grid()
{
	var context = new FakeLayoutProviderContext();

	await new MacroPadIntegration().InitializeAsync(context);

	var grid = context.Layouts["pad-3x2"].PrimaryGrid!.Grid!;
	Assert.Multiple(() =>
	{
		Assert.That((grid.Rows, grid.Columns), Is.EqualTo((2, 3)));
		Assert.That(grid.RowsLocked && grid.ColumnsLocked, Is.True);
	});
}
```

`FakeLayoutProviderContext` keeps the host's rules: re-registering an id replaces it, unregistering an
unknown id is a no-op, and an empty id or name or an empty or repeated region id throws. Assert on
`Layouts` (keyed by local id) and `Calls`. The fake returns the **local** id as `LayoutRegistration.LayoutId`,
not a qualified one, so do not assert on the `::` form.

Against a real plugin process, the harness's `LayoutProvider` client invokes `describe` (`DescribeAsync`)
and `layouts` (`GetLayoutsAsync`). See [Testing](/features/testing/).

## Over the plugin protocol

The host-to-provider direction of the `layout-provider` capability only describes the provider and re-reads
its catalogue after a reconnect:

| Operation | Purpose |
| --- | --- |
| `describe` | The provider's declared name. |
| `layouts` | The provider's current layout catalogue, re-read after a reconnect. |

Registering and withdrawing travel the other way as the `layouts` host API:

| Operation | Purpose |
| --- | --- |
| `register` | Registers a layout, or replaces one already registered under the same provider-local id. |
| `unregister` | Withdraws a layout. Devices still referencing it keep their last-resolved geometry rather than losing their constraint. |

## See also

- [Device providers](/features/devices/) - registering the devices that reference a layout.
- [Folder views](/ui/views/folder-views/) - what `CustomFolderViews` gates.
- [Sizing](/ui/concepts/sizing/) - how widgets size within a grid.
- [Capability parity](/reference/capability-parity/) - plugin-specific behaviour.
