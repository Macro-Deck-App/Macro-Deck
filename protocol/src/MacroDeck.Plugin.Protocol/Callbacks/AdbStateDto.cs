namespace MacroDeck.Plugin.Protocol.Callbacks;

/// <summary>
/// The <c>host.state</c> payload pushed for <see cref="HostApis.Adb" />, per plugin: whether this plugin may use
/// ADB, and the devices when it may. Sent on registration and whenever either changes.
/// </summary>
public sealed record AdbStateDto
{
	/// <summary>One of <see cref="AdbAccessStates" />.</summary>
	public string Access { get; init; } = AdbAccessStates.NotEnabled;

	/// <summary>Empty unless <see cref="Access" /> is <see cref="AdbAccessStates.Available" />.</summary>
	public IReadOnlyList<AdbDeviceStateDto> Devices { get; init; } = [];

	/// <summary>The serial of the device to use when none is chosen, see the SDK's <c>DefaultDevice</c>. It can name a
	/// device absent from <see cref="Devices" />. Absent from older hosts, when there is none, and unless
	/// <see cref="Access" /> is <see cref="AdbAccessStates.Available" />.</summary>
	public string? DefaultDeviceSerial { get; init; }

	/// <summary>True while ADB is available to this plugin but the host cannot find the adb program. Absent from
	/// older hosts and otherwise.</summary>
	public bool? AdbUnavailable { get; init; }

	/// <summary>Same rule as <see cref="DeckStateDto.Revision" />: apply a push only when it is higher than the
	/// last one applied in this session.</summary>
	public long Revision { get; init; }
}

/// <summary>One device in an <see cref="AdbStateDto" /> push. A device that left is simply absent.</summary>
public sealed record AdbDeviceStateDto
{
	public string Serial { get; init; } = string.Empty;

	/// <summary>One of <see cref="AdbDeviceStates" />.</summary>
	public string State { get; init; } = AdbDeviceStates.Offline;

	public string? Model { get; init; }

	public string? Manufacturer { get; init; }

	public string? Product { get; init; }

	/// <summary>A per-installation keyed hash of the device's hardware serial, the same over USB and Wi-Fi, and the
	/// local id the built-in ADB battery provider publishes the device under. Absent from older hosts and until
	/// the host has read it. The hardware serial itself is never sent.</summary>
	public string? StableId { get; init; }

	/// <summary>True when the device is <see cref="AdbDeviceStates.Unauthorized" /> because the host's computer lacks
	/// the USB permissions to talk to it. Absent from older hosts and otherwise.</summary>
	public bool? LacksUsbPermission { get; init; }

	/// <summary>For an <see cref="AdbDeviceStates.Offline" /> device, one of <see cref="AdbDeviceModes" />. Absent from
	/// older hosts and for a device running Android normally; a reader treats an unknown value as
	/// <see cref="AdbDeviceModes.Unknown" />.</summary>
	public string? Mode { get; init; }
}

public static class AdbAccessStates
{
	public const string Available = "available";

	public const string NotEnabled = "adb-not-enabled";

	public const string NotAllowed = "adb-not-allowed";
}

public static class AdbDeviceStates
{
	public const string Online = "online";

	public const string Connecting = "connecting";

	public const string Offline = "offline";

	public const string Unauthorized = "unauthorized";
}

public static class AdbDeviceModes
{
	public const string Bootloader = "bootloader";

	public const string Recovery = "recovery";

	public const string Sideload = "sideload";

	/// <summary>adb reported a state the host does not recognise.</summary>
	public const string Unknown = "unknown";
}
