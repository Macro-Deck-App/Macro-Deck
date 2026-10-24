---
title: Batteries
description: Report battery levels of phones, headphones, controllers and other devices with IBatteryProvider, and read every provider's devices with IIntegrationContext.Batteries - device model, removed and withdrawn devices, stable ids, limits, older hosts, security and testing.
---

Macro Deck keeps one catalog of battery-powered devices: phones, headphones, controllers, mice, keyboards
and the like. A **battery provider** reports the devices it knows and their charge; Macro Deck shows them
in the Battery widget, gives each one variables, and lets every plugin read the whole catalog.

You publish, Macro Deck never asks. The provider polls or listens however its devices need and pushes
what it finds; there is no host-side polling to answer.

## Quick start

An integration implements `IBatteryProvider` next to its other interfaces. The one below reports the
battery of a wireless headset:

```csharp
using MacroDeck.Sdk;
using MacroDeck.Sdk.Batteries;

public sealed class HeadsetIntegration(IHeadsetWatcher watcher) : IPluginIntegration, IBatteryProvider
{
	private IBatteryProviderContext? _batteries;

	public string ProviderName => "Example Headsets";

	public async Task InitializeAsync(IBatteryProviderContext context, CancellationToken cancellationToken = default)
	{
		_batteries = context;
		await context.PublishAsync([.. watcher.Headsets.Select(Describe)], cancellationToken);
		watcher.Changed += OnHeadsetChanged;
	}

	public Task ShutdownAsync(CancellationToken cancellationToken = default)
	{
		watcher.Changed -= OnHeadsetChanged;
		_batteries = null;
		return Task.CompletedTask;
	}

	private void OnHeadsetChanged(object? sender, Headset headset)
		=> _ = _batteries?.PublishAsync(Describe(headset));

	private static BatteryDevice Describe(Headset headset) => new()
	{
		Id = headset.PairingId,
		Name = headset.Name,
		Kind = BatteryDeviceKinds.Headphones,
		LevelPercent = headset.Level,
		ChargeState = headset.IsCharging ? BatteryChargeState.Charging : BatteryChargeState.Discharging,
		IsConnected = headset.IsInRange
	};

	// IPluginIntegration members omitted.
}
```

The headset now appears in the Battery widget's device list, and Macro Deck creates its
`battery_<name>_<hash>_level`, `_charging`, `_connected`, `_state`, `_time_remaining_minutes`,
`_time_to_full_minutes` and `_health` variables under your plugin (see [Variables](#variables)).

Things to know:

- **Order is fixed.** Macro Deck calls `IBatteryProvider.InitializeAsync` after the integration's own
  `InitializeAsync`, and `IBatteryProvider.ShutdownAsync` before the integration stops.
- **The context is yours until `ShutdownAsync` returns.** Keep it and publish whenever something changes.
- **Devices are always published under your plugin or integration.** Nothing in a `BatteryDevice` names
  its owner, so you cannot publish or remove another provider's devices.
- **`ProviderName` is optional.** Left empty, Macro Deck shows your plugin's or integration's name.

## Publishing devices

| `IBatteryProviderContext` member | What it does |
| --- | --- |
| `PublishAsync(device)` | Adds a device, or replaces the one published under the same `Id`. |
| `PublishAsync(devices)` | Adds or replaces several devices in one call. All or nothing: when one is refused, none is applied. An empty list does nothing. |
| `RemoveAsync(deviceId)` | Removes a device for good. Removing an unknown id does nothing. |

`BatteryDevice` is a record with init-only properties. Every property is checked when it is set, so an
instance that exists is always publishable:

| Property | Meaning |
| --- | --- |
| `Id` | Required. Your own stable id for the device - see [Stable ids](#stable-ids). |
| `Name` | Required. The name Macro Deck shows. |
| `Kind` | One of `BatteryDeviceKinds`, `other` by default - see [Kinds](#kinds). |
| `LevelPercent` | 0 to 100, or `null` when the device does not report one. Outside the range throws `ArgumentOutOfRangeException`. |
| `ChargeState` | `Unknown`, `Discharging`, `Charging`, `Full`, or `NotCharging` (on power but not charging). |
| `IsConnected` | Whether the device is reachable right now. `true` by default. |
| `TimeRemaining`, `TimeToFull` | Optional estimates. Negative throws `ArgumentOutOfRangeException`. |
| `Health` | `Unknown`, `Good`, `Overheat`, `Dead`, `OverVoltage`, `Cold` or `Failure`. |

A list that contains `null` or the same id twice throws `ArgumentException`; a publish that would leave you
with more than 64 devices throws `InvalidOperationException` - see [Limits](#limits). The exact bounds are
in
[`BatteryLimits.cs`](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/sdk/src/MacroDeck.Sdk/Batteries/BatteryLimits.cs).

Publish a change when it happens and nothing in between: an unchanged device costs a round trip for
nothing.

### Kinds

`phone`, `tablet`, `laptop`, `headphones`, `watch`, `controller`, `mouse`, `keyboard`, `speaker`, `other`,
as constants on `BatteryDeviceKinds`. The kind picks the icon a reader draws. A reader shows a kind it does
not know as `other`, and a charge state or health it does not know as unknown, so a newer provider keeps
working with an older Macro Deck.

## Connected, withdrawn and removed

A device leaves the catalog in two different ways, and readers treat them differently:

| What happened | Who does it | What readers see |
| --- | --- | --- |
| **Out of reach**: unplugged, out of range, switched off | You publish it with `IsConnected = false` | The device stays, shown as not connected, with its name and last level. Its `_state` reads `disconnected` and both time variables read 0. |
| **Withdrawn**: your provider stopped or was initialized again, the user turned your plugin or integration off, your plugin's session ended, Macro Deck shut down | Macro Deck | The device leaves the list but may come back under the same id. Its variables stay: `_connected` and `_charging` are false, `_state` reads `disconnected`, `_time_remaining_minutes` and `_time_to_full_minutes` read 0, and `_level` and `_health` keep the last reported values; a widget showing it shows its stored name as not available. |
| **Removed for good**: unpaired, forgotten, deleted | You call `RemoveAsync` | The device leaves the list and its id is in `RemovedIds`. Its variables are deleted. |

So keep a device you merely lost contact with published as not connected, and remove it only once it is
gone for good. You never have to clean up on the way out: when your provider stops, Macro Deck withdraws
its devices itself, and the next `InitializeAsync` publishes them again.

**Re-initializing starts from an empty set.** When Macro Deck initializes your provider again while your
plugin stays connected, for example after the user changed its configuration, the devices you published
before are withdrawn first, and the new `InitializeAsync` publishes what the new configuration covers. A
device you no longer report is therefore not left behind, but it is only withdrawn: its variables stay, with
`_connected` false and `_state` reading `disconnected`. Call `RemoveAsync` before the provider stops for a device that is gone for good, such as
one the user removed from your configuration.

## Variables

You write no variable code for your devices. Macro Deck writes the seven variables of every device itself
and files them under the plugin or integration that published it, so they are listed with your other
variables and an Android phone's are listed under the ADB integration. What each one reads is described in
[Battery variables](/guide/concepts/#battery-variables).

- **Do not create these names yourself.** Macro Deck never takes over a variable you created: if yours
  already has a device variable's name, that one variable is not written for the device. `IVariableApi.GetAllAsync` lists them among your variables;
  leave them alone rather than deleting them.
- **They never go stale.** Macro Deck pushes them, so they stay available however long a level stays the
  same, unlike a variable you declare for polling.
- **They follow your devices.** A withdrawn device keeps its variables as not connected, a device you
  remove for good loses them, and they are deleted with the rest of your variables when the user uninstalls
  your plugin.

## Stable ids

`Id` is the device's identity, and readers store it: a Battery widget configured for one device keeps
that device's id, and the device's variable names carry a hash of it. Publishing the same id again
updates the device; a new id is a new device.

- **Derive it from something that stays the same** across restarts and reconnects, such as a pairing id,
  never from a connection handle or a list position.
- **Do not publish a hardware serial or anything else the user would not share.** Every plugin can read
  the id. Hash it with a secret of your own if the stable source is sensitive, the way the
  [ADB provider](#the-adb-provider) does.
- **Readers see a qualified id.** Macro Deck prefixes your id with the provider that published it,
  `<plugin or integration id>:<your id>`, so two providers never collide.

## Reading devices

Every integration reads the whole catalog, whoever published it, through `IIntegrationContext.Batteries`:

| `IBatteryApi` member | What it does |
| --- | --- |
| `Devices` | Every device currently published, as `BatteryDeviceSnapshot`. Empty until Macro Deck has sent the first list. |
| `Find(id)` | The device with that qualified id, or `null`. |
| `Changed` | Raised after `Devices` changed. `RemovedIds` lists the qualified ids removed for good in that change. |

A `BatteryDeviceSnapshot` carries the published fields plus `Id` (qualified), `LocalId`, `ProviderId`,
`ProviderName` and `UpdatedAt`, when Macro Deck last received a report for it.

- **A device absent from `Devices` and not in `RemovedIds` was withdrawn** and may come back under the same
  id. Keep what you stored about it.
- **`RemovedIds` is best effort.** A plugin that was disconnected while a device was removed does not see
  the removal; `Devices` is always complete.
- **Handlers run on a background thread.** One that throws is logged and does not affect the others.
  Handlers subscribed through `IIntegrationContext.Batteries` are released when Macro Deck shuts your
  integration down or initializes it again. In a plugin, `IBatteryApi` can also be taken from dependency
  injection; handlers subscribed there are yours to remove.

A widget that shows the device with the lowest battery, live:

```csharp
using MacroDeck.Sdk;
using MacroDeck.Sdk.Batteries;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Components;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;

public sealed class LowestBatteryIntegration : IPluginIntegration, IUiProvider
{
	private IBatteryApi _batteries = UnsupportedBatteryApi.Instance;

	public Task InitializeAsync(IIntegrationContext context)
	{
		_batteries = context.Batteries;
		return Task.CompletedTask;
	}

	public IReadOnlyList<UiSurfaceDeclaration> Surfaces { get; } =
		[new() { Kind = UiSurfaceKinds.Widget, SessionMode = UiSessionModes.Shared }];

	public Task<IUiSession?> CreateSessionAsync(UiSessionRequest request, CancellationToken cancellationToken)
	{
		if (request.Surface.Kind != UiSurfaceKinds.Widget)
		{
			return Task.FromResult<IUiSession?>(null);
		}

		var batteries = _batteries;
		var lowest = new UiState<BatteryDeviceSnapshot?>(Lowest(batteries.Devices));
		void OnChanged(object? sender, BatteryDevicesChangedEventArgs e) => lowest.Value = Lowest(batteries.Devices);
		batteries.Changed += OnChanged;

		var root = new UiStack
		{
			Key = "lowest",
			Children =
			[
				new UiTextRun { Key = "name", Text = UiText.From(() => lowest.Value?.Name ?? string.Empty) },
				new UiTextRun
				{
					Key = "level",
					Text = UiText.From(() => lowest.Value?.LevelPercent is { } level ? $"{level} %" : "--"),
					Size = 0.4
				}
			]
		};

		return Task.FromResult<IUiSession?>(
			new ViewSession(new UiView(request.Surface, root), onDispose: () => batteries.Changed -= OnChanged));
	}

	private static BatteryDeviceSnapshot? Lowest(IReadOnlyList<BatteryDeviceSnapshot> devices)
		=> devices.Where(device => device is { IsConnected: true, LevelPercent: not null })
			.MinBy(device => device.LevelPercent);

	// Other IPluginIntegration members omitted.
}
```

`ViewSession` is the adapter from [Serving a view](/ui/views/sessions/#example) with one addition: it runs
`onDispose` in `DisposeAsync`, so a closed session stops listening. Writing a `UiState` from the
`Changed` handler is safe on any thread, and only the text that reads it is patched.

## Limits

- **At most 64 devices per provider** at a time (`BatteryLimits.MaxDevicesPerProvider`). A publish that
  would exceed it is refused as a whole; publishing 64 devices in one list is one call.
- **Ids and names** are at most 128 characters, a kind at most 32. Empty or whitespace is refused.
- **A list is all or nothing**, in the SDK and on the host: one invalid device refuses the whole call
  and nothing is applied.
- **Calls count toward your plugin's callback throttle.** The SDK retries a call refused with
  `RATE_LIMITED` once, after a second; a burst beyond that fails. Publish changes, not a timer's every tick.

## In a plugin

`MacroDeck.Plugin.Hosting` declares the `battery-provider` capability for you when an integration
implements `IBatteryProvider`, starts the provider once the plugin is connected and its integration has
initialized, and stops it on shutdown. No permission is needed, to publish or to read.

- **Reconnects are handled.** The SDK remembers the set you published. When the connection drops and the
  session resumes, it resends the whole set in one call, so you publish nothing again. When a new session
  starts instead, Macro Deck initializes your provider again and you publish from scratch.
- **Your devices go with your session.** When your plugin stops or its session ends, Macro Deck withdraws
  the devices that session published. Uninstalling the plugin does the same.
- **A configuration change withdraws too.** Before the SDK initializes your provider again within the
  same session, it asks Macro Deck to withdraw what you published.
- **While the user has turned your plugin off**, what you publish is kept but not shown, and appears again
  when it is turned back on, without a new publish.

## Built-in integrations

A built-in integration implements the same `IBatteryProvider` and reads the same
`IIntegrationContext.Batteries`; nothing crosses a wire. Turning such an integration off stops its
provider and withdraws its devices; turning it on again initializes the provider, which publishes them
again.

### The ADB provider

Macro Deck's own ADB integration is the reference provider. It reports every Android phone and tablet
that has authorized the computer, while ADB is enabled and the ADB integration is turned on. It uses only
the public SDK: the devices, their connection events and their battery readings come from
`IIntegrationContext.AndroidDevices`, the same `IAndroidDeviceManager` a plugin uses (see
[Android devices](/features/android-devices/)), so it would keep working if it were moved into a plugin.
See
[`AdbBatteryProvider.cs`](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/host/src/MacroDeckHost.Integrations/Adb/Providers/AdbBatteryProvider.cs).
It shows the rules on this page in practice:

- **A stable id that reveals nothing.** The id is the device's `StableId`, a keyed hash of its hardware
  serial with a key that belongs to this install of Macro Deck, so the same phone keeps its id over USB and
  Wi-Fi and across restarts, while the serial itself is never published or stored. A widget imported from
  another install therefore shows the device's stored name as not available.
- **Out of reach is not removed.** A phone that disconnects stays published with `IsConnected = false`;
  it is connected again as soon as one of its USB or wireless connections returns. The provider never
  calls `RemoveAsync` on a disconnect.
- **Push on change, poll slowly.** It publishes when a device connects or is authorized and refreshes the
  levels every 15 seconds, publishing only devices that changed.

### The Companion provider

The Macro Deck Companion integration publishes every phone and tablet running the Companion app once it
reported a battery level, from `IIntegrationContext.CompanionDevices` alone (see
[Companion devices](/features/companion-devices/)). See
[`CompanionBatteryProvider.cs`](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/host/src/MacroDeckHost.Integrations/Companion/Providers/CompanionBatteryProvider.cs).

- **One device per Companion configuration.** The id is a hash of the configuration's id, stable across
  reconnects and restarts, and the name is the configuration's title. The kind follows the device's
  [`FormFactor`](/features/companion-devices/#what-a-device-reports): `tablet` or `phone`. A device without
  one is a `tablet` when it is an iPad, a `phone` when it is an iPhone, and `other` otherwise.
- **Events only.** It publishes when a device reports its state, connects or disconnects, and when
  configurations change. A disconnected device stays published as not connected until the integration
  stops; after a restart it returns once it connects and reports a level.
- **Removed only with its configuration.** Deleting the configuration removes the device for good; a
  configuration that needs to be set up again keeps it as not connected. Deleting the last configuration
  turns the integration off before the entry is gone, so its device is withdrawn rather than removed.
- **Charging is a flag.** The app reports whether it charges, so the state is `Charging`, `Full` at 100 percent,
  or `Discharging`; `NotCharging` and `Health` are never reported.

### The System provider

The System integration publishes the computer's own battery: `pmset -g batt` on macOS, `GetSystemPowerStatus`
on Windows, and every `/sys/class/power_supply` entry of type `Battery` with scope `System` (or none) that
is present on Linux. See
[`SystemBatteryProvider.cs`](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/host/src/MacroDeckHost.Integrations/System/Providers/SystemBatteryProvider.cs).

- **Ids without hardware data.** `internal-<n>` (the battery's index or, on Linux, its supply name), named
  after the computer, kind `laptop`. A computer without a battery publishes nothing and stops checking
  after the first read; it checks again the next time the integration starts.
- **Polls every 15 seconds** and publishes only what changed. A read that fails keeps the last values; a
  battery that is gone stays published as not connected and is never removed.

### The Home Assistant provider

The Home Assistant integration publishes only the battery sensors (domain `sensor`, `device_class`
`battery`) the user checked in the **Choose battery sensors** step of its configuration, at most 64. See
[`HomeAssistantBatteryProvider.cs`](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/host/src/MacroDeckHost.Integrations/HomeAssistant/Providers/HomeAssistantBatteryProvider.cs).

- **Ids without entity ids.** The local id is a hash of the entity id, so the entity id is not spelled
  out in the catalog. The hash is not keyed: a plugin that guesses an entity id can confirm it, which tells
  it little beyond the device name every reader sees anyway. A sensor renamed in Home Assistant is a new device until it is
  picked again. The name is the Home Assistant device's, or the sensor's own when one device has several
  picked sensors; the kind is `other`.
- **Charge state from a sibling sensor.** A `binary_sensor` with `device_class` `battery_charging` on the
  same device makes the state `Charging` when on and `Discharging` when off; without one it is `Unknown`.
- **Events, filtered.** It reconciles only when a picked sensor or its charging sensor changes, and on
  connect and disconnect. An unavailable sensor, a sensor missing after a restart, and every sensor while
  Home Assistant is unreachable stay published as not connected.
- **Removed only by the user.** Unchecking a sensor removes it; the integration reloads its configuration
  in place, so the removal reaches readers and deletes its variables.

## Older versions of Macro Deck

- **A plugin on an older host.** A Macro Deck that predates battery providers answers a publish with
  `CAPABILITY_UNSUPPORTED`. The SDK logs this once and turns every later publish into a no-op, so your
  provider keeps running without errors. `IIntegrationContext.Batteries` stays empty and never raises
  `Changed`. Set a minimum host version in [`compatibility.macroDeck`](/reference/manifest/#compatibility)
  if batteries are the point of your plugin.
- **An older plugin on a newer host.** A plugin built before battery providers declares no
  `battery-provider` capability and ignores the `batteries` state the host sends to every plugin.
- **An `IIntegrationContext` of your own**, such as a test double, keeps compiling: `Batteries` is a default
  interface member that returns `UnsupportedBatteryApi.Instance`.

## Security

**Every plugin can read every battery device**, including the ones of other plugins, Android phones
reported over ADB, whether or not plugins may use ADB, and devices running Macro Deck Companion, whether or
not plugins may use Companion devices. That is deliberate: a battery level, a name and a
kind are what a deck shows anyway, and gating them would leave widgets of other plugins blank. So publish
nothing in a name or id that you would not show every plugin: no serial numbers, no account names you
would not put on a deck. See [the security model](/policies/security/#battery-devices).

## Testing

`MacroDeck.Plugin.Testing` has a fake for each side:

```csharp
using MacroDeck.Plugin.Testing.Fakes;
using MacroDeck.Sdk.Batteries;

var batteries = new FakeBatteryProviderContext();
await new HeadsetIntegration(new FakeHeadsetWatcher("pad-1", level: 80)).InitializeAsync(batteries);

Assert.That(batteries.Devices["pad-1"].LevelPercent, Is.EqualTo(80));
Assert.That(batteries.Publishes, Has.Count.EqualTo(1));
```

- **`FakeBatteryProviderContext`** keeps the host's rules: publishing a known id replaces the device, a
  list is all or nothing, and more than 64 devices is refused. Assert on `Devices`, `Publishes` and
  `RemovedIds`.
- **`FakeBatteryApi`** is the reader's side. `Set(devices, removedIds)` replaces the catalog and raises
  `Changed`; `SubscriberCount` tells you whether your code still listens.
- **In a `PluginTestHarness`**, `harness.Context.Batteries` is the `FakeBatteryApi` behind
  `IIntegrationContext.Batteries`, and `harness.Context.BatteryProvider` the `FakeBatteryProviderContext`
  your provider publishes into. `InitializeIntegrationsAsync` starts battery providers against it.

See [Testing](/features/testing/#testing-batteries).

## Over the plugin protocol

| Direction | What | Operations |
| --- | --- | --- |
| Plugin to host | `batteries` host API | `publish` (add or replace up to 64 devices), `remove` (remove ids for good), `sync` (replace the plugin's whole set, used after a resumed reconnect), `withdraw` (withdraw the session's devices, used before a provider is initialized again) |
| Host to plugin | `battery-provider` capability kind, version 1 | `describe` (the provider name) |
| Host to every plugin | `host.state` for `batteries` | The whole catalog with a revision and the ids removed since the previous one |

A call without the declared `battery-provider` capability is `CAPABILITY_UNAVAILABLE`; an invalid device or
a set above the limit is `INVALID_PAYLOAD` and nothing is applied. See
[`batteries` in the WebSocket reference](/reference/websocket/#batteries).

## See also

- [Android devices](/features/android-devices/) - Macro Deck's ADB connection for your own operations.
- [Variables](/features/variables/) - how integration variables work.
- [Widget types](/ui/views/widget-types/) - offering a widget of your own.
- [Testing](/features/testing/) - the fakes and the test harness.
