using MacroDeck.Sdk.Batteries;

namespace MacroDeckHost.Integrations.System.Providers;

internal sealed record HostBatteryReading(
	string Key,
	int? LevelPercent,
	BatteryChargeState ChargeState,
	TimeSpan? TimeRemaining,
	TimeSpan? TimeToFull);
