using MacroDeck.Sdk.Batteries;

namespace MacroDeck.Plugin.Protocol.Callbacks;

/// <summary>
/// The <c>host.state</c> data for <see cref="HostApis.Batteries" />, the same for every plugin: the devices
/// of all providers. Sent on registration and whenever a device changes. A push whose revision is at or
/// below the last one applied in the same session is stale and ignored.
/// </summary>
public sealed record BatteryStateDto
{
	public long Revision { get; init; }

	public IReadOnlyList<BatteryDeviceSnapshotDto> Devices { get; init; } = [];

	/// <summary>
	/// Qualified ids of devices their provider removed for good since the previous revision. A device that
	/// is absent from <see cref="Devices" /> without being listed here was withdrawn and may return. A
	/// plugin that missed a revision may miss its removals; <see cref="Devices" /> is always complete.
	/// </summary>
	public IReadOnlyList<string> RemovedIds { get; init; } = [];
}

/// <summary>One device in a <see cref="BatteryStateDto" />, qualified with the provider that published it.</summary>
public sealed record BatteryDeviceSnapshotDto
{
	/// <summary><c>&lt;provider id&gt;:&lt;local id&gt;</c>.</summary>
	public required string Id { get; init; }

	public required string LocalId { get; init; }

	/// <summary>The plugin or integration that published the device.</summary>
	public required string ProviderId { get; init; }

	public string ProviderName { get; init; } = string.Empty;

	public required string Name { get; init; }

	/// <summary>One of <see cref="BatteryDeviceKinds" />.</summary>
	public string Kind { get; init; } = BatteryDeviceKinds.Other;

	public int? LevelPercent { get; init; }

	/// <summary>One of <see cref="BatteryChargeStates" />.</summary>
	public string ChargeState { get; init; } = BatteryChargeStates.Unknown;

	public bool IsConnected { get; init; }

	public long? TimeRemainingSeconds { get; init; }

	public long? TimeToFullSeconds { get; init; }

	/// <summary>One of <see cref="BatteryHealthStates" />.</summary>
	public string Health { get; init; } = BatteryHealthStates.Unknown;

	public DateTimeOffset UpdatedAt { get; init; }
}
