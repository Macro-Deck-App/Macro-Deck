namespace MacroDeck.Plugin.Protocol.Callbacks;

/// <summary>
/// The <c>host.state</c> payload pushed for <see cref="HostApis.CompanionDevices" />, per plugin: whether this plugin
/// may use Companion devices, and the connected devices when it may. Sent on registration and whenever either changes.
/// </summary>
public sealed record CompanionDevicesStateDto
{
	/// <summary>One of <see cref="CompanionDeviceAccessStates" />.</summary>
	public string Access { get; init; } = CompanionDeviceAccessStates.NotAllowed;

	/// <summary>The devices that have reported their state. Empty unless <see cref="Access" /> is
	/// <see cref="CompanionDeviceAccessStates.Available" />.</summary>
	public IReadOnlyList<CompanionDeviceStateDto> Devices { get; init; } = [];

	/// <summary>Same rule as <see cref="DeckStateDto.Revision" />: apply a push only when it is higher than the
	/// last one applied in this session.</summary>
	public long Revision { get; init; }
}

/// <summary>One device in a <see cref="CompanionDevicesStateDto" /> push. A device that left is simply absent. A value
/// the device does not report is absent; percentages are 0 to 100 and texts at most 64 characters.</summary>
public sealed record CompanionDeviceStateDto
{
	/// <summary>The device's id, a GUID.</summary>
	public string DeviceId { get; init; } = string.Empty;

	/// <summary>Changes exactly when the device becomes ready again after it was gone.</summary>
	public long ReadySequence { get; init; }

	public int? BatteryLevelPercent { get; init; }

	public bool Charging { get; init; }

	/// <summary><c>portrait</c> or <c>landscape</c>.</summary>
	public string? Orientation { get; init; }

	public int? ScreenBrightnessPercent { get; init; }

	public string? Model { get; init; }

	public string? Platform { get; init; }

	public string? AppVersion { get; init; }

	/// <summary><c>phone</c>, <c>tablet</c> or <c>desktop</c>, as the device declared it when it signed in.</summary>
	public string? FormFactor { get; init; }

	/// <summary>Capability names, see the SDK's <c>CompanionDeviceCapabilities</c>. A reader ignores names it does
	/// not know.</summary>
	public IReadOnlyList<string> Capabilities { get; init; } = [];

	public IReadOnlyList<string> RequestableCapabilities { get; init; } = [];

	public bool ConfirmsCommands { get; init; }

	public bool? InFocus { get; init; }

	/// <summary><c>wifi</c>, <c>cellular</c>, <c>ethernet</c>, <c>vpn</c> or <c>none</c>.</summary>
	public string? NetworkType { get; init; }

	public bool? NetworkMetered { get; init; }

	public bool? NetworkValidated { get; init; }

	public string? NetworkName { get; init; }

	public int? CpuUsagePercent { get; init; }

	public int? MemoryUsedPercent { get; init; }
}

public static class CompanionDeviceAccessStates
{
	public const string Available = "available";

	public const string NotAllowed = "not-allowed";
}

/// <summary>Arguments of <c>wait</c>.</summary>
public sealed record CompanionDeviceArguments
{
	public string DeviceId { get; init; } = string.Empty;
}

/// <summary>Arguments of <c>send</c> and <c>request</c>.</summary>
public sealed record CompanionDeviceCommandArguments
{
	public string DeviceId { get; init; } = string.Empty;

	/// <summary>One of <see cref="CompanionDeviceCommands" />.</summary>
	public string Command { get; init; } = string.Empty;

	/// <summary>0 to 100, for <see cref="CompanionDeviceCommands.SetBrightness" />.</summary>
	public int? BrightnessPercent { get; init; }

	/// <summary><c>automatic</c>, <c>portrait</c> or <c>landscape</c>, for
	/// <see cref="CompanionDeviceCommands.SetOrientation" />.</summary>
	public string? Orientation { get; init; }
}

/// <summary>Arguments of <c>screenshot</c>.</summary>
public sealed record CompanionScreenshotArguments
{
	public string DeviceId { get; init; } = string.Empty;

	/// <summary>One of <see cref="CompanionScreenshotModes" />.</summary>
	public string Mode { get; init; } = CompanionScreenshotModes.Deck;
}

/// <summary>Result of <c>wait</c>.</summary>
public sealed record CompanionDeviceWaitResultDto
{
	public bool Ready { get; init; }
}

/// <summary>Result of <c>send</c>: false when the device is not connected.</summary>
public sealed record CompanionDeviceSendResultDto
{
	public bool Sent { get; init; }
}

/// <summary>Result of <c>request</c>.</summary>
public sealed record CompanionDeviceRequestResultDto
{
	/// <summary>One of <see cref="CompanionDeviceOutcomes" />.</summary>
	public string Outcome { get; init; } = CompanionDeviceOutcomes.Failed;

	/// <summary>One of <see cref="CompanionDeviceFailures" /> when <see cref="Outcome" /> is
	/// <see cref="CompanionDeviceOutcomes.Failed" />. A reader treats an unknown value as
	/// <see cref="CompanionDeviceFailures.Failed" />.</summary>
	public string? Failure { get; init; }
}

/// <summary>Result of <c>screenshot</c>: a <see cref="Failure" />, or the PNG's description, whose bytes follow over
/// <c>host.asset.*</c> with <see cref="Assets.AssetKinds.CompanionScreenshot" />.</summary>
public sealed record CompanionScreenshotResultDto
{
	/// <summary>One of <see cref="CompanionDeviceFailures" />; absent when a PNG follows.</summary>
	public string? Failure { get; init; }

	public string? ContentType { get; init; }

	public int ByteLength { get; init; }

	/// <summary>Must match the <c>contentHash</c> of the <c>host.asset.begin</c> that follows.</summary>
	public string? ContentHash { get; init; }

	public string? TransferId { get; init; }
}

public static class CompanionDeviceCommands
{
	public const string SetBrightness = "setBrightness";

	public const string SetOrientation = "setOrientation";

	public const string Vibrate = "vibrate";

	public const string ScreenOn = "screenOn";

	public const string ScreenOff = "screenOff";

	public const string Focus = "focus";
}

public static class CompanionScreenshotModes
{
	public const string Deck = "deck";

	public const string Full = "full";
}

public static class CompanionDeviceOutcomes
{
	public const string Done = "done";

	/// <summary>The device accepted the command but did not confirm it in time.</summary>
	public const string Unconfirmed = "unconfirmed";

	public const string Failed = "failed";
}

public static class CompanionDeviceFailures
{
	public const string NotConnected = "not-connected";

	public const string Removed = "removed";

	public const string TimedOut = "timed-out";

	public const string ConsentDenied = "consent-denied";

	public const string Unavailable = "unavailable";

	public const string Failed = "failed";
}
