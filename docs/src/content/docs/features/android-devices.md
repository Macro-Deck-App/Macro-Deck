---
title: Android devices
description: Work with Android devices through Macro Deck's own ADB connection with IAndroidDeviceManager - the host:adb permission, the user's control, operations, errors, limits and testing.
---

A plugin can run shell commands on an Android device, read its battery, copy files and install or
uninstall apps through Macro Deck's own ADB (Android Debug Bridge) connection. Macro Deck finds the
`adb` program, runs the ADB server and tracks the attached devices; your plugin ships no `adb` client of
its own and never talks to the ADB server directly.

## Quick start

Declare the permission in `manifest.json`:

```json
"permissions": ["host:adb"]
```

Then take `IAndroidDeviceManager` from dependency injection:

```csharp
using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Android;

public sealed class PhoneIntegration(IAndroidDeviceManager android) : IPluginIntegration
{
	public IReadOnlyList<IActionDefinition> Actions => [new SleepPhoneAction(android)];

	// Other IPluginIntegration members omitted.
}

internal sealed class SleepPhoneAction(IAndroidDeviceManager android) : IActionDefinition
{
	public string Id => "sleep-phone";

	public LocalizedText Name => Strings.Actions.SleepPhone();

	public LocalizedText Description => Strings.Actions.SleepPhoneDescription();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
		[ActionParameter.Text("serial", label: Strings.Parameters.Serial(), required: true)];

	public IActionExecutor CreateExecutor() => new Executor(android);

	private sealed class Executor(IAndroidDeviceManager android) : IActionExecutor
	{
		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			if (android.Access != AndroidDeviceAccess.Available)
			{
				return ActionResult.Failed(ActionErrorCodes.PermissionDenied, Strings.Errors.AdbNotAvailable());
			}

			if (context.Parameters.GetValueOrDefault("serial") is not string serial ||
				android.FindDevice(serial) is not { State: AndroidDeviceState.Online } device)
			{
				return ActionResult.Failed(ActionErrorCodes.NotConnected, Strings.Errors.PhoneNotConnected());
			}

			try
			{
				var result = await device.ExecuteShellAsync("input keyevent KEYCODE_SLEEP", context.CancellationToken);
				return result.ExitCode == 0
					? ActionResult.Success()
					: ActionResult.Failed(ActionErrorCodes.ProviderRejected, Strings.Errors.PhoneRefused());
			}
			catch (AndroidDeviceException)
			{
				return ActionResult.Failed(ActionErrorCodes.ProviderError, Strings.Errors.PhoneRefused());
			}
		}
	}
}
```

The user gets a **Sleep phone** action that turns off the screen of the phone with that serial.

Things to know:

- **Resolve it from DI.** `IAndroidDeviceManager` is not on `IIntegrationContext`. Take it in a
  constructor, like any other service; the SDK registers it for every plugin.
- **Check `Access` before you rely on it.** Whether your plugin may use ADB is the user's decision and
  can change while your plugin runs - see [Who decides](#who-decides).
- **Macro Deck owns the devices.** It discovers, connects and forgets them. A plugin never creates,
  connects or disposes a device.

## Declaring `host:adb`

An installed plugin reaches ADB only when its manifest lists `host:adb` in
[`permissions`](/reference/manifest/#permissions). It is the one permission the host enforces; every
other entry in the vocabulary is still declared for disclosure only. If plugins are not allowed to use ADB
when the user installs the first version of your plugin that declares it, Macro Deck asks the user whether
to allow it. It does not ask again for an update of a version that already declared it.

A plugin you run yourself during development, through a
[developer token](/reference/authentication/#developer-token-headless-enrolment) or
[interactive pairing](/reference/authentication/#self-registering-interactive-pairing), is allowed without
the declaration, because you admitted it by hand. Declare it anyway: the packaged plugin needs it.

## Who decides

`Access` is `Available` only when all of these hold:

| Condition | Where the user controls it | `Access` otherwise |
| --- | --- | --- |
| ADB is enabled in Macro Deck | **Settings > ADB**, **Enable ADB** | `AdbNotEnabled` |
| Plugins may use ADB | **Settings > ADB**, **Allow plugins to use ADB** (on by default) | `AdbNotAllowed` |
| The plugin declares `host:adb` | The installed version's manifest | `AdbNotAllowed` |

The user sees the permission before and after installing: the install dialog says the plugin uses ADB,
and the plugin's page lists **Uses ADB** under its capabilities. When a plugin that declares `host:adb` is installed
while **Enable ADB** or **Allow plugins to use ADB** is off, Macro Deck asks the user in a dialog, and
keeps the question in its notifications, whether to turn both on. It asks the same way, once per run of
Macro Deck, when your plugin calls an ADB operation and is refused for either reason, or when ADB is on
but Macro Deck cannot find adb (the call then fails with `AdbUnavailable` and the user is offered the
platform-tools download); a plugin that does not declare `host:adb` is refused without asking. That switch covers every plugin that
declares the permission, not only yours. The user guide describes
all of this in [Plugins and ADB](/guide/usb-connection/#plugins-and-adb).

| `AndroidDeviceAccess` | Meaning |
| --- | --- |
| `Unsupported` | The host has not reported access yet, or predates this API. |
| `Available` | This plugin may use ADB now. |
| `AdbNotEnabled` | ADB is switched off in Macro Deck. |
| `AdbNotAllowed` | ADB is on, but this plugin may not use it. |

Tell the user which of these applies rather than a generic failure: only the user can change it, and the
fix differs. `AccessChanged` is raised whenever the value changes.

## Devices and events

```csharp
android.DeviceConnected += (_, e) => _ = RefreshBatteryAsync(e.Device);
android.DeviceStateChanged += (_, e) =>
{
	if (e.Device.State == AndroidDeviceState.Online && e.PreviousState != AndroidDeviceState.Online)
	{
		_ = RefreshBatteryAsync(e.Device);
	}
};
```

- **`Devices` lists every attached device in any state**, and is empty unless `Access` is `Available`.
  `FindDevice(serial)` returns one of them, or `null`.
- **A device is one stable instance.** The same `IAndroidDevice` stays valid while the device goes
  offline, is unauthorized or reconnects, so you may keep it. `Serial` identifies it.
- **`State`** is `Online` (ready for operations), `Connecting` (waiting for the user to confirm the
  authorization prompt on the device), `Unauthorized`, or `Offline`. Operations need `Online`.
- **`Info`** carries `Model`, `Manufacturer` and `Product` as the device reports them; any of them can be
  `null` until the device is online.
- **`DeviceDisconnected`** is also raised for every device when access is withdrawn.
- **Handlers run on the connection's receive loop.** Keep them short and start real work, such as an
  operation on the device, without awaiting it in the handler. An exception from a handler is logged and
  does not end the connection.

## Operations

| `IAndroidDevice` member | What it does |
| --- | --- |
| `ExecuteShellAsync(command)` | Runs `command` in the device's shell and returns an `AndroidShellResult`. |
| `GetBatteryStateAsync()` | Returns `Level` (0 to 100), `IsCharging`, `Status` and `Health`. |
| `PushFileAsync(localPath, remotePath)` | Copies a file from the computer to the device. |
| `PullFileAsync(remotePath, localPath)` | Copies a file from the device to the computer. |
| `InstallApkAsync(apkPath)` | Installs an APK, replacing an installed version of the same app. |
| `UninstallPackageAsync(packageName)` | Uninstalls an app. |
| `IsPackageInstalledAsync(packageName)` | Whether an app is installed. |

Every operation is a round trip to Macro Deck, so do not call them in a tight loop.

### Shell commands

```csharp
var result = await device.ExecuteShellAsync("getprop ro.build.version.release", cancellationToken);
if (result.ExitCode == 0)
{
	var version = result.StandardOutput.Trim();
}
```

- **A non-zero exit code is a result, not an exception.** Check `ExitCode` yourself.
- **Devices before Android 7 always report exit code 0**, whatever the command did. On those devices,
  judge success from the output.
- **The command runs as given** by the device's shell, with Macro Deck's ADB identity. It must not be
  empty, may be at most 8 KiB, and must not start with `-`.
- **There is no standard input.** A command that waits for input sees end of input at once.
- **Long output is cut.** `Truncated` is `true` when Macro Deck shortened the standard output or error
  to fit one protocol message. Redirect large output to a file on the device and pull it instead.

### Battery

Macro Deck shares one battery reading between all callers, so a result can be a few seconds old. A
device that does not report its battery fails with `Unsupported`.

### Connecting over Wi-Fi

`ConnectAsync("192.168.1.20:5555")` connects Macro Deck's adb to a device with wireless debugging on, the
same as `adb connect`. The address is `host:port` with a host name or IPv4 address, and becomes the
device's serial. It returns the device, which stays attached for every plugin and in **Settings > ADB**
until it disconnects; its `State` can be `Offline` for a moment until Macro Deck reports it `Online`.
Connecting a connected device succeeds. An address adb cannot reach fails with `CommandFailed` and adb's
own message; a malformed one fails with `InvalidArgument` before adb runs. It is refused while Macro Deck
is locked. Users can connect a device themselves in **Settings > ADB**, **Connect over Wi-Fi**; a device
connected either way is visible to both.

Android 11 and later pair a new computer with a code before the first wireless connection. Macro Deck
does not pair; a device that was never paired with this computer, or never switched to network debugging
over USB (`adb tcpip`), refuses the connection.

### Files and apps

- **Paths on the computer are absolute paths on the machine Macro Deck runs on.** Macro Deck reads and
  writes them as the user it runs as, not as your plugin. A file to push or install must exist; the
  directory of a file to pull must exist.
- **Paths on the device are absolute**, starting with `/`.
- **Package names** use Android's grammar, such as `com.example.app`.
- A refused install or uninstall fails with `CommandFailed`, and the message carries adb's own text.

## Errors

Every operation throws `AndroidDeviceException` when it cannot be carried out, and
`OperationCanceledException` when you cancel it. `ErrorCode` says why:

| `AndroidDeviceErrorCode` | Meaning | What to do |
| --- | --- | --- |
| `AdbNotEnabled` | ADB is switched off in Macro Deck. | Ask the user to turn on **Enable ADB**. |
| `AdbNotAllowed` | ADB is on, but this plugin may not use it. | Ask the user to turn on **Allow plugins to use ADB**, or declare `host:adb`. |
| `AdbUnavailable` | Macro Deck cannot find `adb`, or cannot reach its server. | Point the user to **Settings > ADB**. |
| `DeviceNotFound` | No device with this serial is attached. | Wait for `DeviceConnected`. |
| `DeviceOffline` | The device is attached but not reachable. | Wait for `DeviceStateChanged`. |
| `DeviceUnauthorized` | The device has not authorized this computer. | Ask the user to confirm the prompt on the device. |
| `Timeout` | adb did not finish in time. | Retry, or split the work. |
| `CommandFailed` | adb ran and reported a failure, such as a rejected install. | Show the message. |
| `InvalidArgument` | An argument was rejected before anything ran. | Fix the call. |
| `HostUnavailable` | There is no connection to Macro Deck. | Retry after the plugin reconnects. |
| `Unsupported` | The host does not offer ADB to plugins, or the device cannot answer this operation. | Degrade the feature. |
| `HostLocked` | Macro Deck is locked. | Retry after it is unlocked. |
| `RateLimited` | Too many calls at once or in quick succession. | Retry later. |
| `Unknown` | A failure this SDK version does not recognise. | Treat as a failure. |

`AdbNotEnabled` and `AdbNotAllowed` are the user's settings, everything from `AdbUnavailable` to
`CommandFailed` is adb or the device, and `HostLocked` and `RateLimited` are temporary. `Access` tells the
first two apart before you call anything.

## Limits

- **At most four calls in flight per plugin.** A fifth is refused at once with `RateLimited`; it is not
  queued. ADB calls also count toward the plugin's shared callback throttle.
- **While Macro Deck is locked**, shell commands, pushes, pulls, installs, uninstalls and connects are refused with
  `HostLocked`. Battery reads and `IsPackageInstalledAsync` still work.
- **Every operation has a time limit** on the host, about a minute for a shell command or an uninstall,
  five minutes for a push, pull or install, twenty seconds for a connect, and ten seconds for the battery
  and package queries. It then
  fails with `Timeout`. Pass a `CancellationToken` to give up sooner: cancelling also ends the call on the
  host.

## Older hosts

On a Macro Deck that predates this API, `Access` stays `Unsupported`, `Devices` stays empty and every
operation fails with `Unsupported`. A plugin that uses ADB for an optional feature keeps working there with
that feature off. Set a minimum host version in
[`compatibility.macroDeck`](/reference/manifest/#compatibility) if ADB is the point of your plugin.

## Testing

`MacroDeck.Plugin.Testing` has an in-memory `FakeAndroidDeviceManager`:

```csharp
var android = new FakeAndroidDeviceManager();
var phone = android.AddDevice("R58M123", info: new AndroidDeviceInfo("SM-G991B", "samsung", "o1s"));
phone.ShellHandler = command => new AndroidShellResult(0, "14\n", string.Empty, false);

var version = await new PhoneInfo(android).GetAndroidVersionAsync("R58M123");

Assert.That(version, Is.EqualTo("14"));
Assert.That(phone.Calls.Single().Operation, Is.EqualTo(nameof(IAndroidDevice.ExecuteShellAsync)));
```

- **Access starts as `Available`.** `SetAccess` changes it and refuses every operation with the matching
  error, as a real host does. `AddDevice`, `RemoveDevice` and `SetDeviceState` raise the matching events.
- **`FakeAndroidDevice`** records every call in `Calls`. `ShellHandler`, `Battery` and
  `InstalledPackages` script its answers, and `Failure` makes every operation throw with that code.
- **In a `PluginTestHarness`**, register the fake with
  `builder.ConfigureServices((_, services) => services.AddSingleton<IAndroidDeviceManager>(android))`. The
  harness offers no ADB of its own, so without the fake `Access` stays `Unsupported`.

## Security

`host:adb` is user control and consent, not a sandbox. A plugin process runs with the user's full
privileges and could start its own `adb` without asking. What the permission gives the user is a
visible, switchable decision about plugins that use Macro Deck's connection. See
[the security model](/policies/security/#permissions-declared-not-enforced).

## See also

- [WebSocket reference](/reference/websocket/#adb) - the `adb` host API on the wire.
- [Manifest](/reference/manifest/#permissions) - the permission vocabulary.
- [Connect over USB](/guide/usb-connection/) - how users set up ADB.
