using MacroDeck.Sdk.Batteries;

namespace MacroDeckHost.Integrations.System.Providers;

internal readonly record struct WindowsPowerStatus(
	byte AcLineStatus,
	byte BatteryFlag,
	byte BatteryLifePercent,
	uint BatteryLifeTime);

internal static class WindowsPowerStatusMapper
{
	public const string BatteryKey = "0";

	private const byte AcOffline = 0;
	private const byte AcOnline = 1;
	private const byte ChargingFlag = 8;
	private const byte NoSystemBatteryFlag = 128;
	private const byte UnknownStatus = 255;
	private const uint UnknownLifeTime = uint.MaxValue;

	public static IReadOnlyList<HostBatteryReading>? Map(WindowsPowerStatus? status)
	{
		if (status is not { } power || power.BatteryFlag == UnknownStatus)
		{
			return null;
		}

		if ((power.BatteryFlag & NoSystemBatteryFlag) != 0)
		{
			return [];
		}

		int? level = power.BatteryLifePercent <= 100 ? power.BatteryLifePercent : null;
		var chargeState = ChargeStateOf(power, level);
		TimeSpan? remaining = chargeState == BatteryChargeState.Discharging
			&& power.BatteryLifeTime is not (UnknownLifeTime or 0)
				? TimeSpan.FromSeconds(power.BatteryLifeTime)
				: null;

		return [new HostBatteryReading(BatteryKey, level, chargeState, remaining, null)];
	}

	private static BatteryChargeState ChargeStateOf(WindowsPowerStatus power, int? level)
	{
		if ((power.BatteryFlag & ChargingFlag) != 0)
		{
			return BatteryChargeState.Charging;
		}

		return power.AcLineStatus switch
		{
			AcOnline when level == 100 => BatteryChargeState.Full,
			AcOnline => BatteryChargeState.NotCharging,
			AcOffline => BatteryChargeState.Discharging,
			_ => BatteryChargeState.Unknown
		};
	}
}
