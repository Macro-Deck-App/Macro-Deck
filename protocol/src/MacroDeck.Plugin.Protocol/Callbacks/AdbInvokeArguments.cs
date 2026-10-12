namespace MacroDeck.Plugin.Protocol.Callbacks;

/// <summary>Arguments for <c>host.invoke</c> against <see cref="HostApis.Adb" />'s <c>shell</c> operation.</summary>
public sealed record AdbShellArguments
{
	public required string Serial { get; init; }

	/// <summary>Run by the device's shell as given. Must not start with <c>-</c>.</summary>
	public required string Command { get; init; }
}

/// <summary>Arguments for <c>battery</c>, <c>screenshot</c> and <c>status</c>, which take nothing but the device.</summary>
public sealed record AdbDeviceArguments
{
	public required string Serial { get; init; }
}

/// <summary>Arguments for <c>push</c>. <see cref="LocalPath" /> is an absolute path on the host's machine.</summary>
public sealed record AdbPushArguments
{
	public required string Serial { get; init; }

	public required string LocalPath { get; init; }

	/// <summary>An absolute path on the device.</summary>
	public required string RemotePath { get; init; }
}

/// <summary>Arguments for <c>pull</c>. <see cref="LocalPath" /> is an absolute path on the host's machine.</summary>
public sealed record AdbPullArguments
{
	public required string Serial { get; init; }

	/// <summary>An absolute path on the device.</summary>
	public required string RemotePath { get; init; }

	public required string LocalPath { get; init; }
}

/// <summary>Arguments for <c>install</c>. <see cref="ApkPath" /> is an absolute path on the host's machine.</summary>
public sealed record AdbInstallArguments
{
	public required string Serial { get; init; }

	public required string ApkPath { get; init; }
}

/// <summary>Arguments for <c>uninstall</c>, <c>package-installed</c>, <c>start-app</c> and <c>force-stop-app</c>.</summary>
public sealed record AdbPackageArguments
{
	public required string Serial { get; init; }

	public required string PackageName { get; init; }
}

/// <summary>Arguments for <c>connect</c>.</summary>
public sealed record AdbConnectArguments
{
	/// <summary><c>host:port</c> of a device with wireless debugging on, for example <c>192.168.1.20:5555</c>.
	/// The host is a host name or an IPv4 address.</summary>
	public required string Address { get; init; }
}

/// <summary>Result of <c>connect</c>: the serial the device has from now on, which is its address.</summary>
public sealed record AdbConnectResultDto
{
	public string Serial { get; init; } = string.Empty;
}

/// <summary>Result of <c>shell</c>. A non-zero exit code is a result, not an error.</summary>
public sealed record AdbShellResultDto
{
	public int ExitCode { get; init; }

	public string StandardOutput { get; init; } = string.Empty;

	public string StandardError { get; init; } = string.Empty;

	/// <summary>True when the host cut the output to fit one protocol message.</summary>
	public bool Truncated { get; init; }
}

/// <summary>Result of <c>battery</c>.</summary>
public sealed record AdbBatteryStateDto
{
	/// <summary>0 to 100.</summary>
	public int Level { get; init; }

	/// <summary>True while a power source is connected.</summary>
	public bool IsCharging { get; init; }

	/// <summary>One of <see cref="AdbBatteryStatuses" />; an unknown value reads as <c>unknown</c>.</summary>
	public string Status { get; init; } = AdbBatteryStatuses.Unknown;

	/// <summary>One of <see cref="AdbBatteryHealths" />; an unknown value reads as <c>unknown</c>.</summary>
	public string Health { get; init; } = AdbBatteryHealths.Unknown;
}

/// <summary>Result of <c>package-installed</c>.</summary>
public sealed record AdbPackageInstalledDto
{
	public bool Installed { get; init; }
}

/// <summary>Arguments for <c>key</c>.</summary>
public sealed record AdbKeyArguments
{
	public required string Serial { get; init; }

	/// <summary>One of <see cref="AdbKeys" />.</summary>
	public required string Key { get; init; }
}

/// <summary>Arguments for <c>tap</c>, in screen pixels from the top left.</summary>
public sealed record AdbTapArguments
{
	public required string Serial { get; init; }

	public required int X { get; init; }

	public required int Y { get; init; }
}

/// <summary>Arguments for <c>swipe</c>, in screen pixels from the top left.</summary>
public sealed record AdbSwipeArguments
{
	public required string Serial { get; init; }

	public required int FromX { get; init; }

	public required int FromY { get; init; }

	public required int ToX { get; init; }

	public required int ToY { get; init; }

	public required int DurationMilliseconds { get; init; }
}

/// <summary>Arguments for <c>input-text</c>.</summary>
public sealed record AdbTextArguments
{
	public required string Serial { get; init; }

	public required string Text { get; init; }
}

/// <summary>Arguments for <c>open-uri</c>.</summary>
public sealed record AdbUriArguments
{
	public required string Serial { get; init; }

	public required string Uri { get; init; }
}

/// <summary>Arguments for <c>reboot</c>.</summary>
public sealed record AdbRebootArguments
{
	public required string Serial { get; init; }

	/// <summary>One of <see cref="AdbRebootModes" />.</summary>
	public string Mode { get; init; } = AdbRebootModes.Normal;
}

/// <summary>
/// Result of <c>screenshot</c>: metadata only. The PNG bytes follow over <c>host.asset.*</c> with kind
/// <c>android-screenshot</c> under <see cref="TransferId" />, like a <c>devices</c> icon.
/// </summary>
public sealed record AdbScreenshotResultDto
{
	public string ContentType { get; init; } = "image/png";

	public int ByteLength { get; init; }

	/// <summary>Must match the <c>contentHash</c> of the <c>host.asset.begin</c> that follows.</summary>
	public string ContentHash { get; init; } = string.Empty;

	public string TransferId { get; init; } = string.Empty;
}

/// <summary>Result of <c>status</c>. A value the device does not report is absent.</summary>
public sealed record AdbDeviceStatusDto
{
	public bool? ScreenOn { get; init; }

	public bool? Locked { get; init; }

	public string? ForegroundPackage { get; init; }
}

public static class AdbKeys
{
	public const string Home = "home";

	public const string Back = "back";

	public const string Enter = "enter";

	public const string Menu = "menu";

	public const string Search = "search";

	public const string VolumeUp = "volume-up";

	public const string VolumeDown = "volume-down";

	public const string VolumeMute = "volume-mute";

	public const string MediaPlayPause = "media-play-pause";

	public const string MediaNext = "media-next";

	public const string MediaPrevious = "media-previous";

	public const string Power = "power";

	public const string Sleep = "sleep";

	public const string Wakeup = "wakeup";

	public const string AppSwitch = "app-switch";

	public const string DpadUp = "dpad-up";

	public const string DpadDown = "dpad-down";

	public const string DpadLeft = "dpad-left";

	public const string DpadRight = "dpad-right";

	public const string DpadCenter = "dpad-center";

	public static readonly IReadOnlyList<string> All =
	[
		Home, Back, Enter, Menu, Search, VolumeUp, VolumeDown, VolumeMute, MediaPlayPause, MediaNext, MediaPrevious,
		Power, Sleep, Wakeup, AppSwitch, DpadUp, DpadDown, DpadLeft, DpadRight, DpadCenter
	];
}

public static class AdbRebootModes
{
	public const string Normal = "normal";

	public const string Recovery = "recovery";

	public const string Bootloader = "bootloader";
}

public static class AdbBatteryStatuses
{
	public const string Unknown = "unknown";

	public const string Charging = "charging";

	public const string Discharging = "discharging";

	public const string NotCharging = "not-charging";

	public const string Full = "full";
}

public static class AdbBatteryHealths
{
	public const string Unknown = "unknown";

	public const string Good = "good";

	public const string Overheat = "overheat";

	public const string Dead = "dead";

	public const string OverVoltage = "over-voltage";

	public const string Failure = "failure";

	public const string Cold = "cold";
}
