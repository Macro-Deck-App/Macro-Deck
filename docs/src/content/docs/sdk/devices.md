---
title: Device providers
description: Registering hardware and custom clients with Macro Deck from an integration or plugin.
---

A *device provider* brings hardware or a custom client into Macro Deck: a control surface, a macro pad,
an ESP32 panel, a network-connected client of your own. The provider discovers devices however its
transport requires and registers them; Macro Deck keeps owning the device model around them -
persistence, global ids, naming, startup profiles and everything the user sees in device settings.

The contract is transport- and vendor-agnostic. Nothing in it is specific to any device family, and no
host-internal service is exposed across the plugin boundary.

## The contract

Implement `IDeviceProvider` on your integration:

```csharp
public sealed class DeckIntegration : IIntegration, IDeviceProvider
{
	private IDeviceProviderContext? _devices;

	public string ProviderName => "Stream Deck";

	public async Task InitializeAsync(IDeviceProviderContext context, CancellationToken cancellationToken = default)
	{
		_devices = context;

		foreach (var found in await DiscoverAsync(cancellationToken))
		{
			await context.RegisterDeviceAsync(Describe(found), cancellationToken);
		}
	}

	public Task ShutdownAsync(CancellationToken cancellationToken = default) => StopDiscoveryAsync();

	private static DeviceDescriptor Describe(Hardware hardware)
		=> new(hardware.SerialNumber,
			hardware.ProductName,
			Model: hardware.ProductName,
			Manufacturer: "Elgato",
			LayoutReference: "com.example.deck::xl",
			Capabilities: new DeviceCapabilities { KeyCount = 32, SupportsImages = true });
}
```

`IDeviceProviderContext` is the whole host surface:

| Member | Purpose |
| --- | --- |
| `RegisterDeviceAsync` | Offers a device. Returns the host-assigned global id. |
| `UpdateDeviceAsync` | Refreshes a registered device's metadata. |
| `SetDevicePresenceAsync` | Reports whether a device is reachable right now. |
| `UnregisterDeviceAsync` | Withdraws a device from this session. |

`GetDevices()` is optional and reports what the provider currently offers, so the host can recover its
view after a reconnect without waiting for discovery to run again.

## Identity is yours, the device model is the host's

`DeviceDescriptor.Id` is your own stable id for the hardware - a serial number or equivalent. It is the
one thing that makes a device *the same device* later, so derive it from something the hardware itself
carries, never from an enumeration index or a connection handle.

Macro Deck resolves `(your integration or plugin id, that id)` to exactly one device and keeps
everything else about it:

```text
Provider starts   -> registers SERIAL-1        -> host mints a device, or finds the existing one
Hardware away     -> presence Offline, or unregister
Hardware returns  -> registers SERIAL-1 again  -> same device: same global id, name, startup profile
Host restarts     -> registers SERIAL-1 again  -> still the same device
```

Unregistering does **not** delete anything. It ends the runtime registration and takes the device
offline; the device itself is retained so a reconnect is a reuse rather than a new row in the user's
device list. Deleting a device for good is the user's decision, taken in Macro Deck's device settings.
The same applies when your integration stops or your plugin's session drops: its devices go offline and
stay registered.

A provider-registered device never signs in. It holds no credential and no session, so signing it out
is not offered; presence is whatever the provider last reported.

## Capabilities and layouts

`DeviceCapabilities` carries the generic facts every device kind shares - how many keys, dials and
displays it has, whether it can show images or text - plus an `Extra` map for anything specific to your
hardware. Keep it to what a consumer can act on generically.

`LayoutReference` is an **opaque string**: the host stores it and hands it back without interpreting it.
It is where a device says which layout it uses, so that the layout abstraction - which describes
dimensions, regions and their input/output capabilities - stays separate from device discovery. Use the
qualified id (`your.plugin.id::layout-name`) a [layout provider's](/sdk/layouts/) `RegisterLayoutAsync`
returns, so the reference stays unambiguous and resolves to a real layout. The layout does not have to
come from your own provider - a device may reference a layout owned by another plugin - and an
unresolvable reference is never an error: the device still registers, and its profile is simply
unconstrained until a layout with that id is registered.

## Out-of-process plugins

A plugin declares the `device-provider` capability and implements the same `IDeviceProvider`.
`MacroDeck.Plugin.Hosting` starts the provider once the plugin is connected and its integration has
initialized, and stops it on shutdown; registrations travel to the host as callbacks on the `devices`
host API. The provider is always the authenticated plugin - a plugin cannot register or withdraw a
device in another plugin's name.

Declare `host:devices` in `manifest.json` alongside the other host APIs you use.

## Deck surfaces

A provider that only registers devices is complete as it stands - leaving
`OnSessionOpenedAsync` at its default no-op keeps a device registration-only forever. To render a
deck on the hardware and report input back, override it: the host calls it once per registered
device with an `IDeviceSession`, and that session is the whole rendering and input contract.

### Receiving and rendering a session

```csharp
public async Task OnSessionOpenedAsync(IDeviceSession session, CancellationToken cancellationToken = default)
{
	session.SurfaceChanged += (_, e) => Render(session.DeviceId, e.Surface);
	session.Closed += (_, e) => StopRendering(session.DeviceId);

	// The session already carries the first surface - no separate "initial" event.
	Render(session.DeviceId, session.CurrentSurface);
}

// Revisions are per session, so the last one seen is tracked per device rather than per provider.
private readonly ConcurrentDictionary<string, long> _lastRevisions = new(StringComparer.Ordinal);

private void Render(string deviceId, DeviceSurface surface)
{
	// Out-of-order delivery is possible over the transport: drop anything that does not move this
	// device's own revision forward.
	if (surface.Revision <= _lastRevisions.GetValueOrDefault(deviceId))
	{
		return;
	}

	_lastRevisions[deviceId] = surface.Revision;
	DrawGrid(surface.Layout.Rows, surface.Layout.Columns, surface.Layout.WidgetSpacing);

	foreach (var widget in surface.Widgets)
	{
		DrawWidget(widget.PositionX, widget.PositionY, widget.Appearance?.Label, widget.Appearance?.IconId);
	}
}
```

`CurrentSurface` and `SurfaceChanged` deliver the surface to render. Each one is a **complete
snapshot** - the profile, the folder, the effective layout, and every widget currently on it -
never a diff against the last one. `Revision` increases monotonically starting at 1, but only
*within this session*: a reconnect opens a fresh session with its own revision sequence and a full
snapshot again, so do not persist a revision across sessions. Drop a surface whose revision is not
strictly greater than the last one you applied; that is the only signal you get for out-of-order
delivery.

`Layout` is the folder's **effective** grid, already resolved exactly as Macro Deck's own client
resolves it, so you never walk that chain yourself: rows, columns, spacing and border radius come from
the folder, then its ancestors, then the profile defaults, while the background is the folder's own
value or the profile default.

`LayoutReference` is echoed back byte-identical to whatever you declared on
`DeviceDescriptor`/`DeviceRegistration`; the host never parses it. There is deliberately **no
reflow, clipping, or validation** against the device's physical key count: a 3x2 device assigned a
5x3 profile receives the full 5x3 grid, positions included, and fitting that to the hardware -
paging, scrolling, cropping, whatever makes sense for your device - is entirely the provider's job.

`Widgets` includes every widget placed in the folder plus any foreign pinned widget whose scope
reaches it, each listed exactly once. Every label has already been resolved - variables and
templates expanded - and localized in the host's active language; render it as-is rather than
looking it up or translating it yourself.

### Reporting interactions

`SendInteractionAsync` reports hardware input and returns the host's verdict. The host, never the
plugin, resolves the target widget and runs its actions - a provider never sees a flow definition
and never executes one. Because of that, a device press only ever navigates *that device*: if the
widget's action changes folder, the folder change applies to the session that pressed it, and every
other device or client on the same profile keeps whatever it is currently showing.

A widget id in `DeviceInteraction.Target` must come from the surface you are currently rendering.
Two outcomes are normal, not failures - nothing ran, the session stays open, and the next valid
interaction still works:

- `Rejected` with a `DeviceSessionReasons` code, most often `WidgetNotOnSurface` - your press raced
  a surface push. Re-render the newest surface and press again.
- `NotSupported` - the kind is part of the contract but has no widget model yet.

Only `Press`, `Release`, `ShortPress` and `LongPress` execute today; every other
`DeviceInteractionKind` is accepted and reported `NotSupported`.

If your hardware reports raw press/release rather than short/long presses itself, send `Press` on
contact and `Release` on lift; the host synthesizes the rest, matching what the Angular clients do
for a pointer:

- `Press` fires `onTouchStart` immediately and starts a 600 ms timer.
- The timer elapsing while the widget is still held fires `onLongPress`.
- `Release` fires `onTouchEnd`, plus `onShortPress` only if the long press had not already fired.

This state is tracked **per widget**, not per device, so releasing one widget never ends another's
in-flight press, and a second `Press` reported for a widget that is already held is ignored outright
- it does not restart the timer or fire a second `onTouchStart`. If your hardware already
distinguishes a short from a long press itself, report `ShortPress` or `LongPress` directly instead
of a `Press`/`Release` pair; the host does not re-derive what your device already knows, and no
synthesis happens for those kinds.

### Fetching icons

`GetIconAsync` fetches the bytes for an icon referenced by `DeviceSurfaceAppearance.IconId`. Pass
the `knownETag` you already hold to skip the transfer entirely - the result comes back with
`NotModified` set and empty `Content`. Cache by `IconId` **and** `IconVersion`: re-rendering an
icon under the same id leaves the id unchanged, and the version is what tells you the bytes moved
on. Leaving `size` null serves the largest rendered variant, never the original master the icon was
imported from - a device draws onto a key, and an unbounded master would not fit the transfer limit.
An icon over that limit throws `DeviceSessionException` with `DeviceSessionReasons.IconTooLarge`;
the session stays open. The bytes themselves travel over the `host.asset.*` chunked channel, but
that is transport detail - the single `GetIconAsync` call hides it.

### Fetching a provider-controlled icon

A widget's icon does not always come from the icon pack: an action can own the icon a widget
currently renders (album artwork, an avatar, weather imagery). `DeviceSurfaceAppearance.IconId`
stays GUID-only forever, so a provider-owned icon travels a different way. `HasProviderIcon` is
true exactly when the currently rendered icon comes from such a provider, in which case `IconId` is
null. A device provider compiled before this field existed does not know to look for it and simply
renders label and colour, exactly as it would for an icon-less widget. `IconVersion` keeps its
documented meaning either way - the content identity of whatever is currently rendered - so a
caching provider watches the same one key regardless of which kind of icon it turns out to be.

Fetch the bytes with `GetWidgetIconAsync(widgetId, knownETag)`, addressed by the owning widget's own
id rather than by an icon id, since a provider-owned icon has no id of its own to fetch by. It
mirrors `GetIconAsync` in every other respect: pass a `knownETag` you already hold to skip an
unchanged transfer, and the bytes travel the same `host.asset.*` channel. It returns null when the
widget has nothing to serve right now - the provider went inactive, answered blank, or the widget id
is not on this session's current surface - rather than throwing. Default-implemented, the same way
`IWidgetApi.InvalidateIconAsync` is on the plugin side, so a provider written before this member
existed keeps compiling and simply never serves a provider-controlled icon.

### Ending a session

`DisposeAsync` closes the session on the host as well, so a provider that stops serving a device is
not left being pushed to. `Closed` is raised exactly once, whichever side ended the session first -
your own `DisposeAsync`, a host-initiated close, or the device going away.

### For plugins

For a plugin this is the `device-provider` capability's `session.open`, `session.surface` and
`session.close` operations, introduced in **capability version 2**. The host opens a session only
when the negotiated `device-provider` version is 2 or higher; a plugin that negotiates version 1
keeps registering, updating and unregistering devices exactly as before and is never sent a session
operation at all, so an older plugin degrades to registration only rather than failing.

## Testing

`MacroDeck.Plugin.Testing` provides `FakeDeviceProviderContext`, which keeps the host's identity rules:
re-registering a known provider-local id is the same device, and unregistering retains it and only takes
it offline. Drive your provider against it and assert on `Devices`, `IsOnline` and the recorded `Calls`.
`MacroDeckTestHost`'s `DeviceProvider` client invokes the capability's `describe` and `devices`
operations against a plugin under test.
