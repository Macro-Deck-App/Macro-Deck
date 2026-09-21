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
