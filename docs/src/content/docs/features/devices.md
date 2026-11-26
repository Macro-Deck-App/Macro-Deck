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
device stays registration-only forever. The host never waits for it while handling one of your own calls,
so the handler can call back into the host (icons, key images) before it returns.

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
- **An Action Button can carry up to three more labels.** `Appearance.Label` is its primary label and
  `Appearance.ExtraLabels` holds the others in drawing order, each a `DeviceSurfaceLabel` with its own
  resolved `Text`, `Color`, `FontSize` and position `X`/`Y`. `Appearance.LabelX`/`LabelY` carry the
  primary label's position once the widget stores one, which is the case after any save in the editor,
  and are null for a widget that only stores an alignment.
  - A position is a percentage of the button's content box: `0` is flush left or top, `50` centered,
    `100` flush right or bottom. At `X = 25` a quarter of the free space is left of the label, so a label
    never leaves the box.
  - For the primary label, `TextAlign` and `LabelPosition` always name the third of the box the position
    falls in (`left`, `center`, `right` and `top`, `center`, `bottom`), so a provider that draws it at nine
    places only can keep using them. An extra label carries `X`/`Y` alone.
  - The list is empty for every other widget and for a button without extra labels. A provider built
    against an SDK that predates these members never sees them and draws the primary label at
    `TextAlign`/`LabelPosition`, as before; nothing else about the surface changes.
- **A widget with a transparent background arrives without one.** When the user makes a widget's
  background transparent, its `Appearance.BackgroundColor` is null, the same as a widget with no colour of
  its own: draw the folder background behind that key, or your own default where there is none.
  `Layout.BackgroundColor` carries the folder background as the user stored it, which can be a CSS colour
  such as `rgb(...)` or the literal `transparent` rather than `#rrggbb`.
- **Two kinds of colour arrive flattened to an opaque `#rrggbb`:** one that follows a
  [Color variable](/features/variables/#color-variables), resolved to its current value, and one stored as
  translucent, such as `rgba(...)` or `#rrggbbaa` below full opacity, with the alpha dropped. This applies to the layout and widget
  colours alike, and the surface is rebuilt when a referenced variable changes. Every other colour arrives
  exactly as stored.

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
| `Accepted` | The host took it. For a `ShortPress` or `LongPress` on a tile a plugin or integration serves, or on a Slider, it can mean *queued*: see below. On a Slider it does not mean a value was written. |
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

A built-in widget with a Double Tap action changes the `Release` row. The `onShortPress` is held for 400 ms. A second
`Press` in that window that is released before the long press runs `onDoublePress` after its `onTouchEnd`,
and neither tap runs `onShortPress`. If that second press becomes a long press, the held `onShortPress` runs
first, right before `onLongPress`. Navigation, the device going offline and the session closing drop a held
`onShortPress`. Double taps come only from `Press`/`Release` pairs: an explicit `ShortPress` always runs at
once and keeps its verdict, so hardware that reports whole presses never produces `onDoublePress`. A widget
whose only press action is a Double Tap advertises `Press` and `Release` only.

The built-in Countdown and Stopwatch widgets always advertise `Press`, `Release`, `ShortPress` and
`LongPress`, flows or not: a short press starts, pauses, resumes or dismisses the timer and a long press
resets it, and each change runs the widget's own timer flows. Their surface carries the label and colours
only, not the running time.

A widget whose type declares a
[default Short Press action](/ui/views/widget-types/#a-default-short-press-action) advertises `Press`,
`Release`, `ShortPress` and `LongPress` without any flow, and a short press runs that default unless the
user gave the widget a Short Press action of their own. The built-in Weather widget is the exception: its
default opens a dialog, which a hardware deck cannot show, so that default is neither advertised nor run.

A tile that a plugin or integration serves, rather than a built-in one, answers a press from its own UI tree
first, exactly as it does on screen: a [disabled region](/ui/components/modifier/) absorbs the press, and a
control that declares the press receives it instead of the tile's flows. A press the tree does not claim
runs the tile's flows only if its type is registered with
[`SupportsFlows`](/ui/views/widget-types/#running-the-users-actions). Double taps are never recognised on
such a tile: a tree's `double-press` is never sent from a hardware deck, so a button declaring it receives
`press` for every tap, and a Double Tap flow never runs from one. The host asks that tree once per
press and waits at most a second for it; a tree that does not answer in time absorbs the press. Because the
tree may arrive over the same connection your report came in on, the host never holds your report for it:
`Press` and `Release` return at once as always, and a `ShortPress` or `LongPress` whose tree has not answered
yet is answered `Accepted` and runs afterwards, so a failure of that flow no longer comes back as
`Rejected` + `TriggerFailed`. Built-in tiles other than the [Slider](#stepping-a-slider) keep the full verdict.

When the control that claims the press is a `ui.slider` declaring `change`, the press steps it exactly as it
[steps a Slider](#stepping-a-slider). Such a tile advertises only the kinds its flows give it, so a provider
that sends advertised kinds only never steps it.

Press state is tracked **per widget**: releasing one widget never ends another's press, and a second
`Press` for a widget already held is ignored (no timer restart, no second `onTouchStart`). If your hardware
already tells short from long, send `ShortPress`/`LongPress` instead of a `Press`/`Release` pair.

### Stepping a Slider

A key has no analog input, so a short press on the built-in Slider moves it one step further, and a press at
the top wraps it back to the start. The host does the stepping: send an ordinary `ShortPress`, or a
`Press`/`Release` pair, and the Slider writes its variable as if the user had dragged it there. A Slider
always advertises `Press`, `Release` and `ShortPress`, whatever flows it carries. A host that predates this
advertises nothing for a Slider and a press there changes nothing, so a provider that follows
`SupportedInteractions` works against both.

The step is measured on the Slider's track, from `0` at its minimum to `1` at its maximum, using the level
and step the Slider's own [`ui.slider`](/ui/components/slider/) node carries:

- **Press step:** the node's step when it is at least `0.1`. A smaller step is multiplied by the smallest
  whole number that makes it at least `0.1`, so a `0..100` Slider with step `1` moves in tens and stays on
  its own grid. Without a usable step the press step is `0.1`.
- **Next level:** the current level plus the press step, capped at `1`, then snapped to the node's step and
  clamped to `0..1`. A `0..100` Slider with step `1` at `95` goes to `100`, not back to `0`.
- **Wrap:** at the top the next press goes to `0`. The top is `1`, or the last level the step grid reaches
  below it, such as `0.9` for a step of `0.3`, so a press never skips a top the Slider can show.

A few consequences for a provider:

- **Short presses only.** A `Press` held past the long-press threshold is a long press and moves nothing;
  `LongPress` is not advertised. Double taps are not recognised: each tap steps, and a Slider's Double Tap
  action and any press flows it carries do not run from a hardware deck.
- **The verdict.** The press is answered like one on a plugin tile (see above): `Accepted` can mean
  *queued*, and it does not mean the value was written: a variable that cannot be written or a range that is
  not usable refuses the change inside the Slider without a `Rejected`. A locked host still answers
  `Rejected` + `HostLocked`.
- **Each press reads the value the variable has.** Two presses in quick succession can land on the same
  level when the variable has not reported the first write yet. A variable that is unavailable reads as
  its minimum, so a press then steps from there.
- **The key does not show the level.** The surface carries the Slider's label and colours, not its value.

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
- **`IconId` names the image to draw.** When the user's icon has
  [appearances](/guide/concepts/#icon-appearances), `IconId` is the GUID of the appearance Macro Deck chose
  for this device, which can differ from the icon id stored on the widget. If your layout declares
  `Visuals` with `AnimatedIcons` false, that is the icon's static appearance when it has one. Fetch it with
  `GetIconAsync` like any other id; it does not appear in icon listings.
- **`size: null`** serves the largest size variant (512 px), which Macro Deck creates from the master when a
  pack arrived without it. An icon no larger than that is served as it is.
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

## Key images

A deck of display keys does not have to draw the surface itself. Declare `KeyImages` on the layout's grid
and Macro Deck renders every key - widget, label, icon, border, background - into a finished picture
in the size and encoding the hardware takes, and sends you one picture per key.

```csharp
new LayoutGrid
{
	Rows = 3,
	Columns = 5,
	KeySize = new LayoutKeySize(72, 72),
	KeyImages = new LayoutKeyImageFormat { Encoding = KeyImageEncodings.Jpeg, Rotation = 180 }
}
```

```csharp
public async Task OnSessionOpenedAsync(IDeviceSession session, CancellationToken cancellationToken = default)
{
	if (session.KeyImageFormat is null)
	{
		session.SurfaceChanged += (_, e) => Render(session.ProviderDeviceId, e.Surface);
		return;
	}

	var pad = _watcher.Pad(session.ProviderDeviceId);
	session.KeyImageChanged += (_, e) => pad.WriteKey(e.Image.Row, e.Image.Column, e.Image.Content);

	for (var row = 0; row < pad.Rows; row++)
	{
		for (var column = 0; column < pad.Columns; column++)
		{
			if (await session.GetKeyImageAsync(row, column, cancellationToken) is { } image)
			{
				pad.WriteKey(row, column, image.Content);
			}
		}
	}
}
```

- **Opt-in, per layout.** `LayoutGrid.KeyImages` is null by default, and the device then gets surfaces
  only, as before. It needs `KeySize`, which is the size of every delivered picture. A format Macro Deck
  cannot honour - no `KeySize`, a rotation other than 0, 90, 180 or 270, an unknown encoding - means no key
  images for that layout and a warning in the host log, never a failed registration.
- **`IDeviceSession.KeyImageFormat` is the signal.** It is the format the host renders this session in,
  fixed for the life of the session (a layout change takes effect when the session reopens). It is null
  when the layout asks for no pictures, the format cannot be honoured, or the host predates key images,
  so a provider built for both checks it and falls back to rendering `CurrentSurface`.
- **Encodings** are `KeyImageEncodings` values, and each picture's `ContentType` is the matching
  `KeyImageContentTypes` value:

  | Encoding | Content type | Bytes |
  | --- | --- | --- |
  | `rgba` | `application/x-macrodeck-rgba` | 8-bit RGBA, top row first, `width * 4` bytes per row with no padding, always opaque |
  | `png` | `image/png` | PNG |
  | `jpeg` | `image/jpeg` | baseline JPEG, quality 90 |
  | `bmp` | `image/bmp` | 24-bit BMP, rows bottom-up |

- **Orientation:** the key is mirrored left to right first when `MirrorHorizontal` is set, then rotated
  clockwise by `Rotation`. For 90 and 270 it is rendered at the swapped size, so the delivered picture is
  always `KeySize`.
- **Cells are the hardware grid**: `Row` and `Column` count from 0 at the top left over the layout's
  single grid region, rows x columns. Every cell has a picture, an empty key included, and a key the
  current folder does not reach (a smaller folder, no profile assigned) is drawn as background. A widget
  outside the hardware grid is not rendered. A layout with no grid region, or several, gets no key images.
- **First paint:** `GetKeyImageAsync` answers for every cell from the moment the session exists, inside
  `OnSessionOpenedAsync` included, so a device can paint all keys on connect instead of waiting for changes. It returns null for a session without key
  images, for a cell outside the grid, and on a host that does not serve key images - never an exception.
- **Changes:** `KeyImageChanged` carries one cell's new picture, never for a picture that did not change.
  A change Macro Deck rebuilds the surface for (an edit, a widget's state or label, a navigation) has its
  pictures raised after that rebuild: after its `SurfaceChanged`, or right after the host found the surface
  unchanged. A change only the picture shows (a clock tick, live widget content) is raised on its own,
  with no surface; a clock on one key updates that key only.
  `Revision` is per cell, starts at 1 and increases with every change; drop a picture whose revision is not
  greater than the last one you applied for that cell. A session already drops those before it raises the
  event, counting the pictures `GetKeyImageAsync` handed you.
- **Rate:** Macro Deck caps updates at the layout's `Visuals.MaxUpdatesPerSecond`, or 30 per second when
  that is unset.
- **What the visual capabilities change:** with `AnimatedIcons` false a key shows the icon's first frame;
  with `Borders` false no border ring is drawn; corners are rounded only when `CornerRadius` is true and
  keys of a widget spanning several cells are separated by the profile's spacing only when `WidgetSpacing`
  is true.
- **What a key is drawn in:** the host's theme (light draws light, dark and system draw dark, since a
  device has no colour scheme of its own), accent colour, app font and language. Changing any of them, or
  importing or removing a font, draws every key again; only the keys that look different are sent. An
  empty cell takes the folder's empty-cell style, so a transparent one is drawn as background.
- **Widget content:** Macro Deck follows each widget's view the way a client does, one view session per
  widget on the keys, so a widget's provider sees one more viewer. A key waits up to one second for its
  widget's first view; past that it is drawn without the content and updated when the view arrives.
- **Offline devices:** while a device is offline nothing is sent to it, and `GetKeyImageAsync` still
  answers with the last picture drawn for each key; a clock is not redrawn while the device is away.
  When it comes back, every key is drawn again and sent with a new revision, after the surface.
- **Only the widget grid is rendered.** A folder view cannot be drawn onto keys, so declare
  `CustomFolderViews = false` on a key-image layout; the editor then does not offer one for it.
- **Interactions do not change.** Keep reporting presses with the widget ids of `CurrentSurface`, which
  still arrives as before; the picture of a key and the widget at that position come from the same
  surface.

A plugin receives the pictures over `session.key-images` (inline, or over `host.asset.*` when one is too
large for a message) and fetches a cell with the `devices` host API's `key-image`; the session hides both.

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

[Key images](#key-images) need **capability version 3**, which `MacroDeck.Plugin.Hosting` declares from the
SDK version that added them. A plugin that negotiated version 2 is never sent `session.key-images` and
never gets a key-image format on `session.open`: it keeps rendering surfaces. A plugin built against a
newer SDK on an older host sees `KeyImageFormat` null and `GetKeyImageAsync` return null.

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

For [key images](#key-images), set `FakeDeviceProviderContext.KeyImageFormat` before `OpenSession` (or
`FakeDeviceSession.KeyImageFormat` before the provider reads it), push pictures with `PushKeyImage`, which
raises `KeyImageChanged`, and seed `KeyImages` for the answers `GetKeyImageAsync` gives; read back
`KeyImageRequests`. The fake drops nothing, so a test can push a stale revision and check that your
provider ignores it.

Against a real plugin process, the harness's `DeviceProvider` client (`PluginTestHarness`,
`PluginSessionView`) invokes `describe`, `devices`, `session.open`, `session.surface`, `session.key-images`
(`PushKeyImagesAsync`) and `session.close`. Pass a `DeviceSessionOpenArguments` with `KeyImages` set to
`OpenSessionAsync` to open a session the way a host that renders key images does.
See [Testing](/features/testing/).

## Over the plugin protocol

The host-to-provider direction of the `device-provider` capability describes the provider, re-reads its
catalogue after a reconnect, from capability version 2 drives rendering sessions and from version 3
delivers key images:

| Operation | Purpose |
| --- | --- |
| `describe` | The provider's declared name and capability version. |
| `devices` | The provider's current device catalogue, re-read after a reconnect. |
| `session.open` | Opens a device's session and hands over the first surface to render; carries the key-image format when the host renders pictures for the session. Version 2 only. |
| `session.surface` | Pushes a new, complete surface to an already-open session. Version 2 only. |
| `session.close` | Closes an open session. Version 2 only. |
| `session.key-images` | Pushes changed key pictures, several cells per message, each inline or over `host.asset.*`. Version 3 only, and only to a session opened with a key-image format. |

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
| `key-image` | Fetches the current picture of one cell of a session with key images, inline or over the `host.asset.*` pipeline; no value when there is none. |

## See also

- [Layout providers](/features/layouts/) - the geometry a `LayoutReference` points at.
- [Button icons](/features/button-icons/) - where provider-controlled icons come from.
- [Manifest](/reference/manifest/) - `device-provider` and `host:devices`.
- [WebSocket reference](/reference/websocket/) - the wire format for the tables above.
- [Testing](/features/testing/) - the fakes and the test harness.
