using MacroDeck.Sdk.Batteries;

namespace MacroDeck.Plugin.Protocol.Callbacks;

/// <summary>
/// One battery device as its provider publishes it. <see cref="Kind" />, <see cref="ChargeState" /> and
/// <see cref="Health" /> are open strings: a reader treats a kind it does not know as
/// <see cref="BatteryDeviceKinds.Other" /> and a state it does not know as unknown.
/// </summary>
public sealed record BatteryDeviceDto
{
	/// <summary>The provider-local id, at most <see cref="BatteryLimits.MaxIdLength" /> characters.</summary>
	public required string Id { get; init; }

	/// <summary>At most <see cref="BatteryLimits.MaxNameLength" /> characters.</summary>
	public required string Name { get; init; }

	/// <summary>One of <see cref="BatteryDeviceKinds" />.</summary>
	public string Kind { get; init; } = BatteryDeviceKinds.Other;

	/// <summary>0 to 100, or absent when unknown.</summary>
	public int? LevelPercent { get; init; }

	/// <summary>One of <see cref="BatteryChargeStates" />.</summary>
	public string ChargeState { get; init; } = BatteryChargeStates.Unknown;

	public bool IsConnected { get; init; } = true;

	/// <summary>Estimated whole seconds until empty, or absent.</summary>
	public long? TimeRemainingSeconds { get; init; }

	/// <summary>Estimated whole seconds until fully charged, or absent.</summary>
	public long? TimeToFullSeconds { get; init; }

	/// <summary>One of <see cref="BatteryHealthStates" />.</summary>
	public string Health { get; init; } = BatteryHealthStates.Unknown;
}

/// <summary>Arguments of <c>batteries/publish</c>: one to <c>ProtocolLimits.MaxBatteryDevicesPerProvider</c> devices to add or replace.</summary>
public sealed record BatteriesPublishArguments
{
	public IReadOnlyList<BatteryDeviceDto> Devices { get; init; } = [];
}

/// <summary>Arguments of <c>batteries/remove</c>: provider-local ids of devices gone for good. Unknown ids are ignored.</summary>
public sealed record BatteriesRemoveArguments
{
	public IReadOnlyList<string> DeviceIds { get; init; } = [];
}

/// <summary>Arguments of <c>batteries/sync</c>: the plugin's whole set of devices, replacing the previous one.</summary>
public sealed record BatteriesSyncArguments
{
	public IReadOnlyList<BatteryDeviceDto> Devices { get; init; } = [];
}

/// <summary>The values of <see cref="BatteryDeviceDto.ChargeState" />.</summary>
public static class BatteryChargeStates
{
	public const string Unknown = "unknown";

	public const string Discharging = "discharging";

	public const string Charging = "charging";

	public const string Full = "full";

	public const string NotCharging = "not-charging";

	public static readonly IReadOnlyList<string> All = [Unknown, Discharging, Charging, Full, NotCharging];
}

/// <summary>The values of <see cref="BatteryDeviceDto.Health" />.</summary>
public static class BatteryHealthStates
{
	public const string Unknown = "unknown";

	public const string Good = "good";

	public const string Overheat = "overheat";

	public const string Dead = "dead";

	public const string OverVoltage = "over-voltage";

	public const string Cold = "cold";

	public const string Failure = "failure";

	public static readonly IReadOnlyList<string> All = [Unknown, Good, Overheat, Dead, OverVoltage, Cold, Failure];
}
