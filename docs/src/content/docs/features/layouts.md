---
title: Layout providers
description: Registering layouts that describe a device or client surface, and referencing them from a device provider.
---

A *layout provider* describes the surface of a device or client - its regions, their geometry, and what
they can render. A layout says what a surface **is**; it never says what is on it, and it never carries
profile content. Macro Deck reads a registered layout to constrain and validate a profile against real
hardware, and a [device provider](/features/devices/) points a device at one through the existing
`DeviceDescriptor.LayoutReference`. Keeping the two separate is what lets hardware support live entirely
in a plugin, with no device-specific code in Macro Deck itself.

## The contract

Implement `ILayoutProvider` on your integration:

```csharp
public sealed class DeckIntegration : IIntegration, ILayoutProvider, IDeviceProvider
{
	private string? _layoutId;

	public string ProviderName => "Stream Deck";

	public async Task InitializeAsync(ILayoutProviderContext context, CancellationToken cancellationToken = default)
	{
		var registration = await context.RegisterLayoutAsync(
			new LayoutDescriptor(
				"xl",
				"Stream Deck XL",
				Regions:
				[
					new LayoutRegion
					{
						Id = "grid",
						Kind = LayoutRegionKinds.Grid,
						Grid = new LayoutGrid { Rows = 4, Columns = 8, KeySize = new LayoutKeySize(96, 96) }
					}
				],
				Capabilities: new LayoutCapabilities { Visuals = LayoutVisualCapabilities.Full }),
			cancellationToken);

		_layoutId = registration.LayoutId;
	}

	public async Task InitializeAsync(IDeviceProviderContext context, CancellationToken cancellationToken = default)
	{
		foreach (var found in await DiscoverAsync(cancellationToken))
		{
			await context.RegisterDeviceAsync(
				new DeviceDescriptor(found.SerialNumber,
					found.ProductName,
					Model: found.ProductName,
					Manufacturer: "Elgato",
					LayoutReference: _layoutId,
					Capabilities: new DeviceCapabilities { KeyCount = 32, SupportsImages = true }),
				cancellationToken);
		}
	}
}
```

`ILayoutProviderContext` is the whole host surface:

| Member | Purpose |
| --- | --- |
| `RegisterLayoutAsync` | Registers a layout, or replaces one already registered under the same provider-local id. Returns the host-assigned `LayoutRegistration`. |
| `UnregisterLayoutAsync` | Withdraws a layout by its provider-local id. |

`GetLayouts()` is optional and reports what the provider currently offers, so the host can recover its
view after a reconnect without waiting for discovery to run again.

`ILayoutProvider` has no shutdown member of its own. `IDeviceProvider.ShutdownAsync` has the signature it
would need, so a plugin providing both - the ordinary case for hardware, and the one this page's sample
shows - would have written one method body and had it run twice. Release whatever `InitializeAsync`
acquired in your integration's own `ShutdownAsync`; the host withdraws your registered layouts for you.

## Id grammar

`LayoutDescriptor.Id` is a provider-local id, stable across restarts and unique within your provider - a
model name, not a per-device serial. Macro Deck qualifies it with the owning provider into
`your.plugin.id::layout-name`, and that qualified form is what `RegisterLayoutAsync` hands back in
`LayoutRegistration.LayoutId` and what a `DeviceDescriptor.LayoutReference` must carry to resolve to it.

The provider never builds that qualified string itself - it registers the local id and reads the
qualified one back from the registration. This also means a plugin cannot register a layout into another
owner's namespace: the qualifying prefix is always the authenticated provider's own id.

## Associating a layout with a device provider

A device points at a layout through `DeviceDescriptor.LayoutReference` - the same opaque string field
[device providers](/features/devices/#capabilities-and-layouts) already use. The sample above shows the whole
flow: register the layout first, capture the qualified id `RegisterLayoutAsync` returns, and pass it as
`LayoutReference` when registering a device that uses it.

The provider that registers a layout does not have to be the same provider that registers a device
against it. Cross-owner references are allowed - plugin A's device may reference plugin B's layout - because
a layout is descriptive metadata, not a privilege. Nothing about referencing a layout grants access to
the plugin that owns it.

An unresolvable reference is never an error. A device registers successfully even when its
`LayoutReference` names a layout Macro Deck has never seen; its profile is simply left unconstrained
until a layout with that id shows up.

## Regions and capabilities

`LayoutDescriptor.Regions` lists the surface's addressable areas. Each `LayoutRegion.Kind` is one of
`LayoutRegionKinds` - `grid`, `button`, `encoder`, `touch-strip`, `pedal` - but it is an **open string**,
not an enum: a region kind this version has never heard of still round-trips intact with its id, name,
`Count` and `Extra` rather than being dropped. That is what lets a provider declare an encoder bank or a
pedal board today, and lets an older host carry a kind it does not yet understand without discarding the
rest of the layout.

Only `grid` carries typed geometry, in `LayoutRegion.Grid` (a `LayoutGrid`): rows, columns, whether the
size is user-configurable and within what bounds, whether a resize applies live, and the key's pixel
size where the hardware has a fixed one. Every other kind is a count - four encoders, two pedals - rather
than a geometry.

`LayoutDescriptor.PrimaryGrid` is the single `grid` region Macro Deck renders a deck onto, computed for
you: null when the layout declares none (a pedal board, say) or more than one, since Macro Deck will not
guess which surface a profile belongs to.

`LayoutCapabilities.Visuals` (a `LayoutVisualCapabilities`) declares what the surface can actually
render - static and animated icons, borders, background colours, text labels, transparency, and an
advisory `MaxUpdatesPerSecond`. Every flag defaults to `false`, so a layout that declares nothing is
read as able to render nothing rather than everything. A region can override these defaults for itself
through `LayoutRegion.Visuals`; leaving it null means the region inherits the layout's own.

`WidgetSpacing` and `CornerRadius` are the two flags that are not about drawing ability but about
relevance. The host sends a folder's spacing and corner radius to every device on the surface it pushes,
but on hardware whose gaps are physical they change nothing. Set them false and Macro Deck's editor
disables those controls and says the setting has no effect on that device, instead of pretending the
device fixes them to a value the way it fixes rows and columns.

`CustomFolderViews` is a third kind again. It is not about fidelity but about whether the surface can
show a [folder view](/ui/views/folder-views/) - a Macro Deck UI tree standing in for the widget grid - at all.
A surface the host rasterises a key grid for cannot render an arbitrary tree, so a folder set to one
would simply be blank there; Macro Deck therefore does not offer the choice for a profile whose claiming
device says no. Set it true on a layout that describes a software client or a full display surface.

A layout that declares no `Visuals` block at all keeps the option, for the same reason it keeps the
spacing controls: saying nothing must not read as saying no.

## What happens to profiles

Macro Deck reads a resolved layout to *constrain* a profile - it never rewrites one. There is no reflow,
pagination, resizing, or dropping of widgets: a profile bigger than the device it is shown on simply
displays partially on it, exactly as a device session's surface push already does.

A profile is locked to a fixed grid only when the devices that have it as their **startup profile**
resolve to exactly one distinct rows×columns pair. Several devices sharing one layout still lock the
profile; devices whose resolved grids disagree leave it editable instead and surface a warning naming
the disagreeing devices. A layout with zero or with more than one grid region (no `PrimaryGrid`)
constrains nothing, for the same reason - Macro Deck does not guess which grid a profile belongs to.

The host persists the last resolved layout alongside the device, not just the reference string. That is
what lets a fixed-grid device keep constraining its profile while the plugin that provides its layout is
stopped: `UnregisterLayoutAsync` and the provider shutting down both behave the same way here - the
reference is never silently dropped, only left unrefreshed until the layout is registered again.

## Out-of-process plugins

A plugin declares the `layout-provider` capability and implements the same `ILayoutProvider`.
`MacroDeck.Plugin.Hosting` starts the provider once the plugin is connected and its integration has
initialized, and stops it on shutdown; registrations travel to the host as callbacks on the `layouts`
host API. The provider is always the authenticated plugin - a plugin cannot register or withdraw a
layout in another plugin's name, which is what keeps the id-grammar rule above true across the wire as
well as in-process.

Declare `host:layouts` in `manifest.json` alongside the other host APIs you use. See
[Capability parity](/reference/capability-parity/) for plugin-specific behaviour.

A provider is driven from the plugin side for registration, so the host-to-provider direction of the
`layout-provider` capability only has to describe the provider and re-read its catalogue after a
reconnect:

| Operation | Purpose |
| --- | --- |
| `describe` | The provider's declared name. |
| `layouts` | The provider's current layout catalogue, re-read after a reconnect. |

Registering and withdrawing a layout travels the other way as the `layouts` host API:

| Operation | Purpose |
| --- | --- |
| `register` | Registers a layout, or replaces one already registered under the same provider-local id. |
| `unregister` | Withdraws a layout. Devices still referencing it keep their last-resolved geometry rather than losing their constraint. |

## Testing

`MacroDeck.Plugin.Testing` provides a fake layout provider context that keeps the host's identity rules:
re-registering a known provider-local id replaces the layout in place, and unregistering an unknown id is
a no-op rather than an error. Drive your provider against it and assert on the layouts it holds.
`MacroDeckTestHost` exposes a client that invokes the `layout-provider` capability's `describe` and
`layouts` operations against a plugin under test, the same way its device-provider client does for
`IDeviceProvider`.
