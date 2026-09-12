---
title: Device providers
description: Bring hardware or a custom client into Macro Deck with IDeviceProvider - register devices, report presence, render the deck and send presses back.
---

A device provider brings hardware or a custom client into Macro Deck: a control surface, a macro pad, an
ESP32 panel. You discover devices and register them; Macro Deck owns everything else about them -
persistence, global ids, naming, startup profiles and the device settings the user sees.

## Quick start

```csharp
using MacroDeck.Sdk;
using MacroDeck.Sdk.Devices;

public sealed class MacroPadIntegration(IPadWatcher watcher) : IPluginIntegration, IDeviceProvider
{
	private readonly IPadWatcher _watcher = watcher;

	public string ProviderName => "Macro Pad";

	public async Task InitializeAsync(IDeviceProviderContext context, CancellationToken cancellationToken = default)
	{
		foreach (var pad in _watcher.ConnectedPads())
		{
			await context.RegisterDeviceAsync(Describe(pad), cancellationToken);
		}

		_watcher.Attached += (_, pad) => _ = context.RegisterDeviceAsync(Describe(pad));
		_watcher.Detached += (_, pad) => _ = context.SetDevicePresenceAsync(pad.SerialNumber, DevicePresence.Offline);
		_watcher.Start();
	}

	public Task ShutdownAsync(CancellationToken cancellationToken = default) => _watcher.StopAsync();

	private static DeviceDescriptor Describe(Pad pad)
		=> new(pad.SerialNumber,
			pad.ProductName,
			Model: pad.ProductName,
			Manufacturer: "Example",
			LayoutReference: "com.example.macropad::pad-3x2",
			Capabilities: new DeviceCapabilities { KeyCount = 6, SupportsImages = true });

	// IPluginIntegration members omitted.
}
```

Every connected pad now shows up in Macro Deck's device settings, where the user can name it and give it
a startup profile. Unplugging it takes it offline; plugging it back in is the same device.

Things to know:

- **`DeviceDescriptor.Id` is the device's identity.** Derive it from something the hardware carries (a
  serial number), never from an enumeration index or a connection handle.
- **Every later call takes that provider-local id** - `SetDevicePresenceAsync`, `UpdateDeviceAsync`,
  `UnregisterDeviceAsync`. The host's global id comes back in `DeviceRegistration.DeviceId`.
- **Order is fixed.** The host calls `IDeviceProvider.InitializeAsync` after the integration's own
  `InitializeAsync` (in a plugin, also after its `ILayoutProvider`), and `IDeviceProvider.ShutdownAsync`
  before the integration stops.
- **Registration only is complete.** Rendering a deck on the hardware is opt-in - see
  [Rendering a session](#receiving-and-rendering-a-session).

## Registering a device

| `IDeviceProviderContext` member | What it does |
| --- | --- |
| `RegisterDeviceAsync` | Offers a device, or re-registers a known provider-local id as the same device. Returns `DeviceRegistration(DeviceId, ProviderDeviceId)`. Throws `ArgumentException` for an empty id or name. |
| `UpdateDeviceAsync` | Refreshes a registered device's metadata. Unknown devices are ignored. |
| `SetDevicePresenceAsync` | Reports whether a device is reachable right now. |
| `UnregisterDeviceAsync` | Withdraws a device from this session. The device is retained. |

| `DeviceDescriptor` parameter | Meaning |
| --- | --- |
| `Id` | Stable provider-local id. Non-empty, unique within your provider. |
| `Name` | Proposed name. A name the user has set wins. |
| `Model`, `Manufacturer` | Shown in device settings. |
| `LayoutReference` | The layout the device uses - see [Capabilities and layouts](#capabilities-and-layouts). |
| `Capabilities` | `DeviceCapabilities`: `KeyCount`, `DialCount`, `DisplayCount`, `SupportsImages`, `SupportsText`, plus an `Extra` map for hardware-specific facts. |
| `Presence` | `Online` (default), `Offline` or `Unknown` at registration. |
| `Metadata` | Provider-defined, opaque to the host. |

The context is safe to keep until `ShutdownAsync` returns. The contract is transport- and vendor-agnostic;
no host-internal service crosses the plugin boundary.

## Presence and reconnects

```text
Provider starts   -> registers SERIAL-1        -> host mints a device, or finds the existing one
Hardware away     -> presence Offline, or unregister
Hardware returns  -> registers SERIAL-1 again  -> same device: same global id, name, startup profile
Host restarts     -> registers SERIAL-1 again  -> still the same device
```

Macro Deck resolves `(your plugin id, Id)` to exactly one device. Re-registering keeps its global id, the
user's name and its startup profile; the descriptor refreshes the rest.

**Nothing you do deletes a device.** Unregistering takes it offline and stops offering it; the device stays
so a reconnect is a reuse, not a new row. The same happens when your integration stops or your plugin's
session drops, so you do not have to unregister everything on the way out. Deleting a device for good is
the user's decision, in device settings.

A provider-registered device never signs in: it holds no credential or session, sign-out is not offered,
and its presence is whatever you last reported.

Implement `GetDevices()` to return what you currently offer; the host reads it to recover its view after a
reconnect without waiting for discovery. The default returns an empty list.

## Capabilities and layouts

```csharp
var layout = await layouts.RegisterLayoutAsync(PadLayout, cancellationToken);   // ILayoutProviderContext
await devices.RegisterDeviceAsync(Describe(pad) with { LayoutReference = layout.LayoutId }, cancellationToken);
```

`LayoutReference` is an **opaque string**: the host stores it and hands it back unparsed. Use the qualified
id (`your.plugin.id::layout-name`) that a [layout provider's](/features/layouts/) `RegisterLayoutAsync`
returns. The layout may belong to another plugin. An unresolvable reference is never an error: the device
registers, and its profile stays unconstrained until a layout with that id is registered.

Keep `DeviceCapabilities` to facts a consumer can act on generically; geometry and rendering ability belong
in the layout.

## Receiving and rendering a session

```csharp
private readonly ConcurrentDictionary<string, long> _lastRevisions = new(StringComparer.Ordinal);

public Task OnSessionOpenedAsync(IDeviceSession session, CancellationToken cancellationToken = default)
{
	session.SurfaceChanged += (_, e) => Render(session.ProviderDeviceId, e.Surface);
	session.Closed += (_, e) => StopRendering(session.ProviderDeviceId);

	Render(session.ProviderDeviceId, session.CurrentSurface);
	return Task.CompletedTask;
}

private void Render(string serial, DeviceSurface surface)
{
	if (surface.Revision <= _lastRevisions.GetValueOrDefault(serial))
	{
		return;
	}

	_lastRevisions[serial] = surface.Revision;
	_watcher.Pad(serial).Clear(surface.Layout.Rows, surface.Layout.Columns, surface.Layout.BackgroundColor);

	foreach (var widget in surface.Widgets)
	{
		_watcher.Pad(serial).DrawKey(widget.PositionX, widget.PositionY, widget.Appearance?.Label,
			widget.Appearance?.BackgroundColor);
	}
}
```

The host calls `OnSessionOpenedAsync` once per registered device. Leave it at its default no-op and the
device stays registration-only forever.

- **Every surface is a complete snapshot** - profile, folder, effective layout, every widget - never a diff.
  `CurrentSurface` is the first one; there is no separate "initial" event.
- **`Revision` starts at 1 and increases within this session only.** A reconnect opens a fresh session with
  a new sequence and a full snapshot. Drop any surface whose revision is not strictly greater than the last
  one you applied - it is your only out-of-order signal. Never persist a revision.
- **`Layout` is already resolved:** rows, columns, spacing and border radius come from the folder, then its
  ancestors, then the profile defaults; the background is the folder's own or the profile default.
- **No reflow, clipping or validation.** A 3x2 device given a 5x3 profile gets the full 5x3 grid, positions
  included; paging, scrolling or cropping is yours. `Layout.LayoutReference` echoes your declared reference
  byte for byte.
- **`Widgets`** holds every widget in the folder plus any foreign pinned widget whose scope reaches it, each
  once. Labels are already resolved and localized - render them as-is.

## Reporting interactions

```csharp
var result = await session.SendInteractionAsync(new DeviceInteraction
{
	Kind = DeviceInteractionKind.Press,
	Target = new DeviceInteractionTarget { WidgetId = widget.Id },
	SurfaceRevision = session.CurrentSurface.Revision
});

if (result.ReasonCode == DeviceSessionReasons.WidgetNotOnSurface)
{
	Render(session.ProviderDeviceId, session.CurrentSurface);
}
```

The host resolves the widget and runs its actions; a provider never sees or executes a flow. A press only
navigates *that* device: a folder change applies to the pressing session, and other devices on the same
profile keep what they show. `SurfaceRevision` lets the host discard a press aimed at a superseded surface.

The widget id must come from the surface you are rendering. A refusal is normal - nothing ran, the session
stays open, the next press works:

| Result | Meaning |
| --- | --- |
| `Accepted` | The host took it. For a `ShortPress` or `LongPress` on a tile a plugin or integration serves, it can mean *queued*: see below. |
| `Rejected` + `WidgetNotOnSurface` | The press raced a surface push. Re-render the newest surface. |
| `Rejected` + `HostLocked` | The host is locked and runs nothing until unlocked. |
| `Rejected` + `TriggerFailed` | The widget was edited or deleted between push and press. |
| `Rejected` + `SessionNotFound` | The host no longer holds the session; `Closed` follows. |
| `NotSupported` | A contract kind with no widget model yet. |

Only `Press`, `Release`, `ShortPress` and `LongPress` execute today; every other `DeviceInteractionKind` is
answered `NotSupported`.

| You send | The host fires |
| --- | --- |
| `Press` | `onTouchStart` immediately, and starts a 600 ms timer. |
| (timer elapses while held) | `onLongPress`. |
| `Release` | `onTouchEnd`, plus `onShortPress` if the long press had not fired. |
| `ShortPress` / `LongPress` | That trigger directly - no synthesis. |

A tile that a plugin or integration serves, rather than a built-in one, answers a press from its own UI tree
first, exactly as it does on screen: a [disabled region](/ui/components/modifier/) absorbs the press, and a
control that declares the press receives it instead of the tile's flows. The host asks that tree once per
press and waits at most a second for it; a tree that does not answer in time absorbs the press. Because the
tree may arrive over the same connection your report came in on, the host never holds your report for it:
`Press` and `Release` return at once as always, and a `ShortPress` or `LongPress` whose tree has not answered
yet is answered `Accepted` and runs afterwards, so a failure of that flow no longer comes back as
`Rejected` + `TriggerFailed`. Built-in tiles keep the full verdict.

Press state is tracked **per widget**: releasing one widget never ends another's press, and a second
`Press` for a widget already held is ignored (no timer restart, no second `onTouchStart`). If your hardware
already tells short from long, send `ShortPress`/`LongPress` instead of a `Press`/`Release` pair.

## Fetching icons

```csharp
var appearance = widget.Appearance;
if (appearance?.IconId is { } iconId)
{
	var cached = _icons.GetValueOrDefault((iconId, appearance.IconVersion));
	var image = await session.GetIconAsync(iconId, size: 72, knownETag: cached?.ETag);
	if (image is { NotModified: false })
	{
		_icons[(iconId, appearance.IconVersion)] = image;
	}
}
```

- **`knownETag`** skips an unchanged transfer: the result has `NotModified` set and empty `Content`.
- **Cache by `IconId` and `IconVersion`.** Re-rendering an icon keeps its id; the version says the bytes
  changed.
- **`size: null`** serves the largest rendered variant, never the imported master.
- **Too large** throws `DeviceSessionException` with `ReasonCode` `IconTooLarge`; the session stays open.

The bytes travel the `host.asset.*` chunked channel; `GetIconAsync` hides that.

## Fetching a provider-controlled icon

```csharp
if (appearance is { HasProviderIcon: true })
{
	var image = await session.GetWidgetIconAsync(widget.Id, knownETag: cachedETag);
	// null: nothing to serve right now - render label and colour instead.
}
```

An action can own the icon a widget renders (album artwork, an avatar, weather imagery). `IconId` stays
GUID-only forever, so such an icon has none: `HasProviderIcon` is true exactly when the rendered icon comes
from a provider, and `IconId` is then null. `IconVersion` is the content identity of whatever is rendered,
either kind, so a cache keys on the same value.

`GetWidgetIconAsync` addresses the owning widget and otherwise mirrors `GetIconAsync` (`knownETag`, same
channel). It returns null rather than throwing when the provider went inactive, answered blank, or the
widget is not on the current surface. It is default-implemented to return null, and a provider built
before `HasProviderIcon` existed simply renders label and colour, as for an icon-less widget.

## Ending a session

```csharp
await session.DisposeAsync();
```

`DisposeAsync` closes the session on the host too, so you are not pushed to any more. `Closed` is raised
exactly once, whichever side ends it first - your `DisposeAsync`, a host-initiated close, or the device
going away. `DeviceSessionClosedEventArgs.Reason` may be null.

## In a plugin

Declare the `device-provider` capability and `host:devices` in [`manifest.json`](/reference/manifest/).
`MacroDeck.Plugin.Hosting` starts the provider once the plugin is connected and its integration has
initialized, and stops it on shutdown. The provider is always the authenticated plugin: it cannot register
or withdraw a device in another plugin's name.

Sessions need `device-provider` **capability version 2**. The host opens a session only when the negotiated
version is 2 or higher; a plugin that negotiates version 1 keeps registering, updating and unregistering
as before and is never sent a session operation - it degrades to registration only.

## Testing

```csharp
using MacroDeck.Plugin.Testing.Fakes;
using MacroDeck.Sdk.Devices;

[Test]
public async Task A_replugged_pad_is_the_same_device_and_renders_its_surface()
{
	var context = new FakeDeviceProviderContext();
	var integration = new MacroPadIntegration(new FakePadWatcher("SERIAL-1"));

	await integration.InitializeAsync(context);
	var firstId = context.AssignedIdOf("SERIAL-1");

	await context.UnregisterDeviceAsync("SERIAL-1");
	await context.RegisterDeviceAsync(new DeviceDescriptor("SERIAL-1", "Macro Pad"));

	var session = await context.OpenSession(integration, "SERIAL-1");
	session.PushSurface(new DeviceSurface
	{
		Revision = 1,
		Layout = new DeviceSurfaceLayout { Rows = 2, Columns = 3 },
		Widgets = []
	});

	Assert.Multiple(() =>
	{
		Assert.That(context.AssignedIdOf("SERIAL-1"), Is.EqualTo(firstId));
		Assert.That(context.IsOnline("SERIAL-1"), Is.True);
		Assert.That(session.Interactions, Is.Empty);
	});
}
```

`FakeDeviceProviderContext` keeps the host's identity rules: re-registering a known id is the same device,
unregistering retains it and only takes it offline. Assert on `Devices`, `IsOnline`, `AssignedIdOf`,
`Calls` and `Interactions`.

`OpenSession` hands your provider a `FakeDeviceSession` (the device must be registered first). Drive it with
`PushSurface` and `Close`, script the verdict with `NextResult`, and seed `Icons` / `WidgetIcons`; read back
`Interactions`, `IconRequests` and `WidgetIconRequests`.

Against a real plugin process, the harness's `DeviceProvider` client (`PluginTestHarness`,
`PluginSessionView`) invokes `describe`, `devices`, `session.open`, `session.surface` and `session.close`.
See [Testing](/features/testing/).

## Over the plugin protocol

The host-to-provider direction of the `device-provider` capability describes the provider, re-reads its
catalogue after a reconnect and, from capability version 2, drives rendering sessions:

| Operation | Purpose |
| --- | --- |
| `describe` | The provider's declared name and capability version. |
| `devices` | The provider's current device catalogue, re-read after a reconnect. |
| `session.open` | Opens a device's session and hands over the first surface to render. Version 2 only. |
| `session.surface` | Pushes a new, complete surface to an already-open session. Version 2 only. |
| `session.close` | Closes an open session. Version 2 only. |

Registration, interactions and icon fetches travel the other way as the `devices` host API:

| Operation | Purpose |
| --- | --- |
| `register` | Registers a device, or re-registers a known provider-local id as the same device. |
| `update` | Refreshes a registered device's metadata. |
| `presence` | Reports whether a registered device is currently reachable. |
| `unregister` | Withdraws a device from this session; the device itself is retained. |
| `interaction` | Reports a hardware interaction from an open device session. |
| `icon` | Fetches icon bytes referenced by a device's current surface, over the `host.asset.*` pipeline. |
| `widget-icon` | Fetches the bytes behind a widget's currently rendered action-icon-provider icon, over the `host.asset.*` pipeline. |
| `close` | Closes an open device session at the provider's own request. |

## See also

- [Layout providers](/features/layouts/) - the geometry a `LayoutReference` points at.
- [Button icons](/features/button-icons/) - where provider-controlled icons come from.
- [Manifest](/reference/manifest/) - `device-provider` and `host:devices`.
- [WebSocket reference](/reference/websocket/) - the wire format for the tables above.
- [Testing](/features/testing/) - the fakes and the test harness.
