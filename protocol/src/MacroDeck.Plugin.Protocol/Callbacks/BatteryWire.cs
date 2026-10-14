using MacroDeck.Sdk.Batteries;

namespace MacroDeck.Plugin.Protocol.Callbacks;

/// <summary>
/// Converts the SDK's battery types to and from their <c>batteries</c> wire shapes, so the host and the
/// plugin side read the open strings the same way: a kind, charge state or health a reader does not know
/// reads as <see cref="BatteryDeviceKinds.Other" /> or unknown.
/// </summary>
public static class BatteryWire
{
	public static BatteryDeviceDto ToDto(BatteryDevice device)
	{
		ArgumentNullException.ThrowIfNull(device);

		return new BatteryDeviceDto
		{
			Id = device.Id,
			Name = device.Name,
			Kind = device.Kind,
			LevelPercent = device.LevelPercent,
			ChargeState = ToWire(device.ChargeState),
			IsConnected = device.IsConnected,
			TimeRemainingSeconds = ToSeconds(device.TimeRemaining),
			TimeToFullSeconds = ToSeconds(device.TimeToFull),
			Health = ToWire(device.Health)
		};
	}

	/// <summary>Reads a published device back into the SDK type, which validates every field.</summary>
	/// <exception cref="ArgumentException">A field is out of bounds; the whole call it came with is invalid.</exception>
	public static BatteryDevice ToDevice(BatteryDeviceDto dto)
	{
		ArgumentNullException.ThrowIfNull(dto);

		return new BatteryDevice
		{
			Id = dto.Id,
			Name = dto.Name,
			Kind = dto.Kind,
			LevelPercent = dto.LevelPercent,
			ChargeState = ToChargeState(dto.ChargeState),
			IsConnected = dto.IsConnected,
			TimeRemaining = FromSeconds(dto.TimeRemainingSeconds, nameof(dto.TimeRemainingSeconds)),
			TimeToFull = FromSeconds(dto.TimeToFullSeconds, nameof(dto.TimeToFullSeconds)),
			Health = ToHealth(dto.Health)
		};
	}

	public static BatteryDeviceSnapshotDto ToDto(BatteryDeviceSnapshot snapshot)
	{
		ArgumentNullException.ThrowIfNull(snapshot);

		return new BatteryDeviceSnapshotDto
		{
			Id = snapshot.Id,
			LocalId = snapshot.LocalId,
			ProviderId = snapshot.ProviderId,
			ProviderName = snapshot.ProviderName,
			Name = snapshot.Name,
			Kind = snapshot.Kind,
			LevelPercent = snapshot.LevelPercent,
			ChargeState = ToWire(snapshot.ChargeState),
			IsConnected = snapshot.IsConnected,
			TimeRemainingSeconds = ToSeconds(snapshot.TimeRemaining),
			TimeToFullSeconds = ToSeconds(snapshot.TimeToFull),
			Health = ToWire(snapshot.Health),
			UpdatedAt = snapshot.UpdatedAt
		};
	}

	/// <summary>Reads a pushed device leniently: unknown strings read as their fallback, an out-of-range level or a negative estimate as unknown.</summary>
	public static BatteryDeviceSnapshot ToSnapshot(BatteryDeviceSnapshotDto dto)
	{
		ArgumentNullException.ThrowIfNull(dto);

		return new BatteryDeviceSnapshot
		{
			Id = dto.Id,
			LocalId = dto.LocalId,
			ProviderId = dto.ProviderId,
			ProviderName = dto.ProviderName,
			Name = dto.Name,
			Kind = BatteryDeviceKinds.Normalize(dto.Kind),
			LevelPercent = dto.LevelPercent is >= 0 and <= 100 ? dto.LevelPercent : null,
			ChargeState = ToChargeState(dto.ChargeState),
			IsConnected = dto.IsConnected,
			TimeRemaining = dto.TimeRemainingSeconds is >= 0
				? TimeSpan.FromSeconds(dto.TimeRemainingSeconds.Value)
				: null,
			TimeToFull = dto.TimeToFullSeconds is >= 0 ? TimeSpan.FromSeconds(dto.TimeToFullSeconds.Value) : null,
			Health = ToHealth(dto.Health),
			UpdatedAt = dto.UpdatedAt
		};
	}

	public static string ToWire(BatteryChargeState state)
	{
		return state switch
		{
			BatteryChargeState.Discharging => BatteryChargeStates.Discharging,
			BatteryChargeState.Charging => BatteryChargeStates.Charging,
			BatteryChargeState.Full => BatteryChargeStates.Full,
			BatteryChargeState.NotCharging => BatteryChargeStates.NotCharging,
			_ => BatteryChargeStates.Unknown
		};
	}

	public static BatteryChargeState ToChargeState(string? value)
	{
		return value switch
		{
			BatteryChargeStates.Discharging => BatteryChargeState.Discharging,
			BatteryChargeStates.Charging => BatteryChargeState.Charging,
			BatteryChargeStates.Full => BatteryChargeState.Full,
			BatteryChargeStates.NotCharging => BatteryChargeState.NotCharging,
			_ => BatteryChargeState.Unknown
		};
	}

	public static string ToWire(BatteryHealth health)
	{
		return health switch
		{
			BatteryHealth.Good => BatteryHealthStates.Good,
			BatteryHealth.Overheat => BatteryHealthStates.Overheat,
			BatteryHealth.Dead => BatteryHealthStates.Dead,
			BatteryHealth.OverVoltage => BatteryHealthStates.OverVoltage,
			BatteryHealth.Cold => BatteryHealthStates.Cold,
			BatteryHealth.Failure => BatteryHealthStates.Failure,
			_ => BatteryHealthStates.Unknown
		};
	}

	public static BatteryHealth ToHealth(string? value)
	{
		return value switch
		{
			BatteryHealthStates.Good => BatteryHealth.Good,
			BatteryHealthStates.Overheat => BatteryHealth.Overheat,
			BatteryHealthStates.Dead => BatteryHealth.Dead,
			BatteryHealthStates.OverVoltage => BatteryHealth.OverVoltage,
			BatteryHealthStates.Cold => BatteryHealth.Cold,
			BatteryHealthStates.Failure => BatteryHealth.Failure,
			_ => BatteryHealth.Unknown
		};
	}

	private static long? ToSeconds(TimeSpan? value)
	{
		return value is { } time ? (long)time.TotalSeconds : null;
	}

	private static TimeSpan? FromSeconds(long? seconds, string name)
	{
		if (seconds is null)
		{
			return null;
		}

		if (seconds < 0 || seconds > (long)TimeSpan.MaxValue.TotalSeconds)
		{
			throw new ArgumentOutOfRangeException(name,
				seconds,
				"A battery time estimate is a non-negative number of seconds.");
		}

		return TimeSpan.FromSeconds(seconds.Value);
	}
}
