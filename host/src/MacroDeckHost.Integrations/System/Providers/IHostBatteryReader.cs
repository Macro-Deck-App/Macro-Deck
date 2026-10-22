namespace MacroDeckHost.Integrations.System.Providers;

internal interface IHostBatteryReader
{
	// Null when the platform could not answer this time; an empty list when the computer has no battery.
	Task<IReadOnlyList<HostBatteryReading>?> ReadAsync(CancellationToken cancellationToken);
}
