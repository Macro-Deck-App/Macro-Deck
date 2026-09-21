namespace MacroDeck.Plugin.Protocol.Callbacks;

/// <summary>Arguments for <c>host.invoke</c> against <see cref="HostApis.Adb" />'s <c>shell</c> operation.</summary>
public sealed record AdbShellArguments
{
	public required string Serial { get; init; }

	/// <summary>Run by the device's shell as given. Must not start with <c>-</c>.</summary>
	public required string Command { get; init; }
}

/// <summary>Arguments for <c>battery</c>, which takes nothing but the device.</summary>
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

/// <summary>Arguments for <c>uninstall</c> and <c>package-installed</c>.</summary>
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
