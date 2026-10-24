---
title: Companion devices
description: Read what the phones and tablets connected through the Companion app report, send them commands and take screenshots with ICompanionDeviceApi - the host:companion-devices permission, the user's control, events, results, limits and testing.
---

A plugin can read what the phones and tablets connected through the Macro Deck Companion app report about
themselves (battery, orientation, brightness, network, model and more), send them commands such as
**Vibrate** or **Set screen brightness**, and capture a screenshot of their deck or their whole screen.
Macro Deck owns the connections: your plugin never connects, pairs or removes a device, and never talks to
the app directly.

## Quick start

Declare the permission in `manifest.json`:

```json
"permissions": ["host:companion-devices"]
```

Then take `ICompanionDeviceApi` from dependency injection:

```csharp
using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Companion;

public sealed class TabletIntegration(ICompanionDeviceApi companion) : IPluginIntegration
{
	public IReadOnlyList<IActionDefinition> Actions => [new DimTabletAction(companion)];

	// Other IPluginIntegration members omitted.
}

internal sealed class DimTabletAction(ICompanionDeviceApi companion) : IActionDefinition
{
	public string Id => "dim-tablet";

	public LocalizedText Name => Strings.Actions.DimTablet();

	public LocalizedText Description => Strings.Actions.DimTabletDescription();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
		[ActionParameter.Text("device", label: Strings.Parameters.Device(), required: true)];

	public IActionExecutor CreateExecutor() => new Executor(companion);

	private sealed class Executor(ICompanionDeviceApi companion) : IActionExecutor
	{
		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			if (companion.Access != CompanionDeviceAccess.Available)
			{
				return ActionResult.Failed(ActionErrorCodes.PermissionDenied, Strings.Errors.CompanionNotAllowed());
			}

			if (context.Parameters.GetValueOrDefault("device") is not string text ||
				!Guid.TryParse(text, out var deviceId) ||
				companion.FindDevice(deviceId) is not { } device)
			{
				return ActionResult.Failed(ActionErrorCodes.NotConnected, Strings.Errors.TabletNotConnected());
			}

			var command = CompanionDeviceCommand.SetBrightness(10);
			if (!device.State.ConfirmsCommands)
			{
				return await companion.SendAsync(deviceId, command, context.CancellationToken)
					? ActionResult.Success()
					: ActionResult.Failed(ActionErrorCodes.NotConnected, Strings.Errors.TabletNotConnected());
			}

			var result = await companion.RequestAsync(deviceId, command, context.CancellationToken);
			return result.Outcome == CompanionDeviceCommandOutcome.Failed
				? ActionResult.Failed(ActionErrorCodes.ProviderError, Strings.Errors.TabletRefused())
				: ActionResult.Success();
		}
	}
}
```

The user gets a **Dim tablet** action that turns the brightness of the device with that id down to 10 %.

Things to know:

- **Resolve it from DI, or read `context.CompanionDevices`.** Take it in a constructor, like any other
  service; the SDK registers it for every plugin. `IIntegrationContext.CompanionDevices` returns the same
  instance, for code that only has the context.
- **Check `Access` before you rely on it.** Whether your plugin may use Companion devices is the user's
  decision, off until they allow it, and can change while your plugin runs - see [Who decides](#who-decides).
- **Macro Deck owns the devices.** A device appears when the app connects and reports its state, and
  disappears when it disconnects or the user removes it.

## Declaring `host:companion-devices`

An installed plugin reaches Companion devices only when its manifest lists `host:companion-devices` in
[`permissions`](/reference/manifest/#permissions). It is one of the two permissions the host enforces,
next to [`host:adb`](/features/android-devices/#declaring-hostadb); every other entry in the vocabulary is
still declared for disclosure only. If plugins are not allowed to use Companion devices when the user
installs the first version of your plugin that declares it, Macro Deck asks the user whether to allow it.
It does not ask again for an update of a version that already declared it.

A plugin you run yourself during development, through a
[developer token](/reference/authentication/#developer-token-headless-enrolment) or
[interactive pairing](/reference/authentication/#self-registering-interactive-pairing), counts as declaring
it, because you admitted it by hand. Declare it anyway: the packaged plugin needs it. The user's switch
still applies to such a plugin.

## Who decides

`Access` is `Available` only when both of these hold:

| Condition | Where the user controls it | `Access` otherwise |
| --- | --- | --- |
| Plugins may use Companion devices | **Settings > Companion App**, **Plugin access**, **Allow plugins to use Companion devices** (off by default) | `NotAllowed` |
| The plugin declares `host:companion-devices` | The installed version's manifest | `NotAllowed` |

The user sees the permission before and after installing: the install dialog says the plugin uses the
devices connected through Macro Deck Companion, and the plugin's page lists **Uses Companion devices**.
When a plugin that declares `host:companion-devices` is installed while **Allow plugins to use Companion
devices** is off, Macro Deck asks the user in a dialog, **A plugin wants to use your Companion devices**, and
keeps the question in its notifications; **Allow** turns the setting on. It asks the same way, once per run
of Macro Deck, when your plugin calls an operation and is refused; a plugin that does not declare
`host:companion-devices` is refused without asking. That switch covers every plugin that declares the
permission, not only yours. It does not cover a device's battery: the built-in Companion integration
publishes it to the [battery catalog](/features/batteries/#the-companion-provider), which every plugin reads
without a permission. The user guide describes all of this in
[Plugins and your Companion devices](/guide/companion-app/tips/#plugins-and-your-companion-devices).

| `CompanionDeviceAccess` | Meaning |
| --- | --- |
| `Unsupported` | The host has not reported access yet, or predates this API. |
| `Available` | This plugin may use Companion devices now. |
| `NotAllowed` | The user has not allowed plugins to use Companion devices, or the manifest does not declare `host:companion-devices`. |

Tell the user what to turn on rather than a generic failure: only the user can change it.
`AccessChanged` is raised whenever the value changes.

## Devices and events

```csharp
private readonly ConcurrentDictionary<Guid, long> _greeted = new();

companion.DeviceReady += (_, e) =>
{
	if (e.Device is { } device && _greeted.GetValueOrDefault(device.Id) != device.ReadySequence)
	{
		_greeted[device.Id] = device.ReadySequence;
		_ = companion.SendAsync(device.Id, CompanionDeviceCommand.Vibrate);
	}
};
```

- **`Devices` lists the connected devices that have reported their state**, and is empty unless `Access`
  is `Available`. `FindDevice(id)` returns one of them, or `null`.
- **A device is a snapshot.** `CompanionDevice` is a record of `Id`, `ReadySequence` and `State`. A new
  report replaces it, so read `FindDevice` again rather than keeping an old instance.
- **`Id`** is the device's id in Macro Deck, a GUID, the same id its deck client and its configuration
  in the built-in Companion integration use.
- **`ReadySequence`** changes every time the device becomes ready again after it was gone, and only then.
  Two `DeviceReady` events with the same value describe the same connection; remember the last value per
  device, as above, to do something once per connection.
- **`DeviceReady`** is raised when a device reports its first state since it connected, and for every
  device in `Devices` when access becomes `Available`.
- **`DeviceChanged`** is raised after a device reported its state, and with `Device` set to `null` when it
  disconnected or was removed, and for every device when access is withdrawn.
- **`WaitForDeviceAsync(id)`** waits for a device that has just connected to report its state. It answers
  `true` at once for a device in `Devices`, and `false` when the device has no open connection or does not
  report within a few seconds.
- **Handlers run on a background thread**, in the order the changes arrived. One that throws is logged and
  does not affect the others.

### What a device reports

`State` is a `CompanionDeviceState`. A value Macro Deck could not read, or that the device does not
report, is `null`; percentages are 0 to 100 and texts at most 64 characters.

| Property | Meaning |
| --- | --- |
| `BatteryLevelPercent`, `IsCharging` | The battery. |
| `Orientation` | `portrait` or `landscape` (`CompanionOrientations`). |
| `ScreenBrightnessPercent` | The screen brightness. |
| `Model`, `Platform`, `AppVersion` | The device model, its operating system with version (for example `Android 14`), and the Companion app version. |
| `FormFactor` | `phone`, `tablet` or `desktop` (`CompanionFormFactors`), as the device declared it when it signed in. |
| `InFocus` | Whether Macro Deck is in front on the device. |
| `NetworkType` | `wifi`, `cellular`, `ethernet`, `vpn` or `none` (`CompanionNetworkTypes`). |
| `NetworkMetered`, `NetworkValidated` | Whether the network is metered, and whether it reaches the internet. |
| `NetworkName` | The Wi-Fi network's name, only with the `networkName` capability. |
| `CpuUsagePercent`, `MemoryUsedPercent` | Only with the `cpu` and `memory` capabilities. |
| `Capabilities` | What the device can do right now. |
| `RequestableCapabilities` | What it can do after asking its user, see [Capabilities and device prompts](#capabilities-and-device-prompts). |
| `ConfirmsCommands` | Whether the device confirms commands, see [Send or request](#send-or-request). |

## Operations

| `ICompanionDeviceApi` member | What it does |
| --- | --- |
| `SendAsync(id, command)` | Sends a command without waiting for the device to carry it out. `false` when the device is not connected or `Access` is not `Available`. |
| `RequestAsync(id, command)` | Sends a command and waits for the device's answer, a `CompanionDeviceCommandResult`. |
| `CaptureScreenshotAsync(id, mode)` | Captures the deck or the whole screen as a PNG, a `CompanionScreenshotResult`. |
| `WaitForDeviceAsync(id)` | Waits for a device that has just connected to report, see [above](#devices-and-events). |

Commands are created with the static members of `CompanionDeviceCommand`:

| Command | What it does | Needs |
| --- | --- | --- |
| `Vibrate` | Vibrates the device. | - |
| `SetBrightness(percent)` | Sets the screen brightness, 0 to 100; anything else throws `ArgumentOutOfRangeException`. | - |
| `SetOrientation(orientation)` | `automatic`, `portrait` or `landscape`; anything else throws `ArgumentException`. | - |
| `ScreenOn` | Turns the screen on and shows this Macro Deck. | `screenOn` |
| `ScreenOff` | Turns the screen off. | `screenOff` |
| `Focus` | Brings Macro Deck to the front on the device. | `focus` |

What each platform supports is in the user guide's
[What Macro Deck can do with a device](/guide/companion-app/tips/#what-macro-deck-can-do-with-a-device).

### Send or request

`SendAsync` hands the command to the device and returns; it cannot tell whether the device carried it out.
`RequestAsync` waits for the answer, and is the right call only for a device whose `ConfirmsCommands` is
`true`, or for a capability in `RequestableCapabilities`, where the device asks its user first. A device
that does not confirm leaves every request to wait until it times out.

| `CompanionDeviceCommandOutcome` | Meaning |
| --- | --- |
| `Done` | The device confirmed it carried the command out. |
| `Unconfirmed` | The device accepted the command but did not confirm it in time, so it may or may not have run. |
| `Failed` | The command did not run; `Failure` says why. |

The device has a minute to accept a command and a further few seconds to confirm it once accepted.
Cancelling the token withdraws a command the device has not accepted yet and throws
`OperationCanceledException`; once the device accepted it, the request waits for the answer.

### Capabilities and device prompts

`Capabilities` and `RequestableCapabilities` hold the names in `CompanionDeviceCapabilities`: `screenOn`,
`screenOff`, `focus`, `screenshotDeck`, `screenshotFull`, `networkName`, `cpu` and `memory`. A later Macro
Deck may report names that are not listed; treat an unknown name as one your plugin does not use.

A capability in `RequestableCapabilities` is one the device can use once its user grants a permission on
the device, such as Android's permission to turn the screen off. A request that needs it shows that prompt
on the device and fails with `ConsentDenied` when the user declines. `RequestableCapabilities` never
contains a name that is already in `Capabilities`.

### Screenshots

```csharp
var shot = await companion.CaptureScreenshotAsync(deviceId, CompanionScreenshotMode.Deck, cancellationToken);
if (shot.Png is { } png)
{
	await File.WriteAllBytesAsync(path, png, cancellationToken);
}
```

- **`Deck`** captures only the deck, without asking the device's user. It needs `screenshotDeck`, and fails
  with `Unavailable` while the deck is not on screen.
- **`FullScreen`** captures the whole screen. It needs `screenshotFull`, which the device grants only after
  its user agreed on the device, so a capture can show a prompt there and fails with `ConsentDenied` when
  the user declines. Allowing plugins in Macro Deck does not change that.
- **A capture waits like a request**, with up to a minute once the device accepted it.
- **`Png` is set exactly when `Failure` is `null`.** A screenshot larger than 8 MiB fails with `Failed` for
  a plugin; it travels in chunks after the reply.

### Failures

`RequestAsync` and `CaptureScreenshotAsync` do not throw for a refused or failed call; they answer with a
`CompanionDeviceFailure`. Only cancelling your token throws.

| `CompanionDeviceFailure` | Meaning | What to do |
| --- | --- | --- |
| `NotConnected` | The device is not connected or disconnected before it answered, or the host does not offer Companion devices. | Wait for `DeviceReady`. |
| `Removed` | The user removed the device while the request was running. | Forget the device. |
| `TimedOut` | The device did not answer in time. | Retry, or use `SendAsync` for a device that does not confirm. |
| `ConsentDenied` | The device's user declined the permission the request needs. | Tell the user to allow it on the device. |
| `Unavailable` | The device cannot do this right now, such as a deck screenshot while the deck is not on screen. | Retry later. |
| `Failed` | The device tried and failed, or the host refused the call. | Treat as a failure. |
| `NotAllowed` | This plugin may not use Companion devices. | See [Who decides](#who-decides). |

Later versions may add values; treat an unknown one like `Failed`.

## Limits

- **At most four calls in flight per plugin.** A fifth is refused at once rather than queued, which a
  request or screenshot reports as `Failed` and a send as `false`. Calls also count toward the plugin's
  shared callback throttle. Other host APIs keep flowing while a request waits for a device.
- **A screenshot is at most 8 MiB** for a plugin.
- **The SDK gives up after the host's own windows plus a margin**: about 15 seconds for
  `WaitForDeviceAsync`, 20 for `SendAsync`, and two and a half minutes for `RequestAsync` and
  `CaptureScreenshotAsync`. Pass a `CancellationToken` to give up sooner: cancelling also ends the call on
  the host.

## Integrations built into Macro Deck

Macro Deck's own integrations reach Companion devices through the same `ICompanionDeviceApi`, read from
`IIntegrationContext.CompanionDevices` (see [ADR 0107](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/decisions/0107-built-in-integrations-are-built-as-extractable-plugins.md)).
For them `Access` is always `Available`: they are part of Macro Deck, so neither **Allow plugins to use
Companion devices** nor `host:companion-devices` applies, and `AccessChanged` is never raised. Should such
an integration ever move out of process, it becomes a plugin like any other and needs the permission and
the user's consent.

The built-in Macro Deck Companion integration uses nothing else. Its actions send the commands and
screenshots above, its variables read each device's `State`, and its **device ready** event is published
from `DeviceReady`, once per `ReadySequence`. See
[`CompanionIntegration.cs`](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/host/src/MacroDeckHost.Integrations/Companion/CompanionIntegration.cs).

## A configuration entry per device

An integration whose configuration is per device can ask Macro Deck to keep one configuration entry for
each Companion device by implementing the member-less `ICompanionDeviceConfigEntries` next to its config
flow:

```csharp
public sealed class TabletIntegration : IPluginIntegration, IConfigFlowProvider, ICompanionDeviceConfigEntries
{
	// IPluginIntegration and IConfigFlowProvider members omitted.
}
```

What Macro Deck does with it:

- **Creates the entry when a device connects.** When a device running the Companion app connects and has no
  usable entry yet, Macro Deck stores one with the device id as its id, the device's name as its title and
  empty values. Storing it switches the integration on, as any stored entry does, unless the user switched
  the integration off; a switch-off made while the entry is being created is kept.
- **Deletes it when the device is removed** from Macro Deck, without recording that as a switch-off.
- **One owner.** Only one integration gets these entries: the first integration built into Macro Deck that
  implements the interface, or, when there is none, the first plugin that does, in the order they register.
  Macro Deck keeps that choice in memory for as long as it runs and makes it again on every start. While the
  built-in Companion integration is present a plugin never gets them. When the owner is switched off, no
  entry is created for anyone.
- **Your plugin needs access.** A plugin owner also needs `Access` to be `Available` (see
  [Who decides](#who-decides)); without it no entry is created, and entries follow once the user allows it.
- **Prepared like any entry.** If your plugin also implements `IConfigEntryLifecycle`, Macro Deck asks it to
  prepare the entry first, exactly as for an entry the user completes. A refusal leaves the device without an
  entry, and Macro Deck tries again with the device's next report.

On the wire, implementing the interface declares the `companion-device-entries` capability. It is
declarative: its only operation, `describe`, answers without data. A Macro Deck that predates it rejects the
declaration non-fatally and creates no entries for your plugin.

## Older hosts

On a Macro Deck that predates this API, `Access` stays `Unsupported`, `Devices` stays empty, no event is
raised, `SendAsync` and `WaitForDeviceAsync` answer `false`, and `RequestAsync` and
`CaptureScreenshotAsync` fail with `NotConnected`. Nothing throws. A Macro Deck that has the API but predates
`FormFactor` leaves it `null`. An `IIntegrationContext` you implemented
yourself against an older SDK, such as a test double, keeps compiling: `CompanionDevices` is a default
interface member that returns `UnsupportedCompanionDeviceApi.Instance`, which behaves the same way. A
plugin that uses Companion devices for an optional feature keeps working there with that feature off. Set
a minimum host version in [`compatibility.macroDeck`](/reference/manifest/#compatibility) if Companion
devices are the point of your plugin.

## Testing

`MacroDeck.Plugin.Testing` has an in-memory `FakeCompanionDeviceApi`:

```csharp
var companion = new FakeCompanionDeviceApi();
var tablet = Guid.NewGuid();
companion.Connect(tablet, new CompanionDeviceState { ConfirmsCommands = true, BatteryLevelPercent = 80 });

await new TabletDimmer(companion).DimAsync(tablet);

Assert.That(companion.Calls.Single().Command, Is.EqualTo(CompanionDeviceCommand.SetBrightness(10)));
```

- **Access starts as `Available`** with no devices. `SetAccess` changes it and raises `AccessChanged`;
  while it is `NotAllowed`, requests and screenshots fail with `NotAllowed`, as on a real host.
- **`Connect`** adds or reconnects a device with a new `ReadySequence` and raises `DeviceChanged` and
  `DeviceReady`; **`Report`** replaces what it reports and raises `DeviceChanged`; **`Disconnect`** removes
  it and raises `DeviceChanged` with no device. Events are raised synchronously on the calling thread.
- **`OnRequest` and `OnScreenshot`** decide how requests and screenshots end. A request answers `Done`
  and a screenshot fails with `Unavailable` until you set them.
- **`Calls`** records every send, request and screenshot in order, refused ones included.
- **In a `PluginTestHarness`**, `harness.Context.CompanionDevices` is the plugin's fake, both through DI
  and through `context.CompanionDevices`. Its `Access` starts as `Unsupported`, like a host that has not
  reported yet; call `SetAccess(CompanionDeviceAccess.Available)` and `Connect` to script it.

See [Testing](/features/testing/#testing-companion-devices).

## Security

`host:companion-devices` is user control and consent, not a sandbox. A plugin process runs with the
user's full privileges. What the permission gives the user is a visible, switchable decision, off until
they turn it on, about plugins that read their phones' state, send them commands and capture their
screens through Macro Deck. A plugin that is allowed sees everything listed in
[What a device reports](#what-a-device-reports), including the Wi-Fi network's name, and a deck screenshot
shows whatever the deck shows, other plugins' widgets included. A full screen screenshot still needs the
device's own user to agree on the device. See
[the security model](/policies/security/#companion-devices).

## Over the plugin protocol

| Direction | What | Operations |
| --- | --- | --- |
| Plugin to host | `companion-devices` host API | `wait` (a device that just connected), `send` (fire and forget), `request` (wait for the answer), `screenshot` (the PNG follows over `host.asset.*` with kind `companion-screenshot`) |
| Host to plugin | `host.state` for `companion-devices`, per plugin | `access` (`available` or `not-allowed`), the devices when available, and a revision |
| Plugin to host | `companion-device-entries` capability, declared by `ICompanionDeviceConfigEntries` | `describe`, answered without data |

A call while the plugin's access is not `available` is refused with `COMPANION_DEVICES_NOT_ALLOWED`. See
[`companion-devices` in the WebSocket reference](/reference/websocket/#companion-devices).

## See also

- [WebSocket reference](/reference/websocket/#companion-devices) - the `companion-devices` host API on the wire.
- [Manifest](/reference/manifest/#permissions) - the permission vocabulary.
- [Companion app tips](/guide/companion-app/tips/) - what users do with Companion devices.
- [Android devices](/features/android-devices/) - Macro Deck's ADB connection, the other enforced permission.
- [Batteries](/features/batteries/) - battery levels of every device, readable without a permission.
