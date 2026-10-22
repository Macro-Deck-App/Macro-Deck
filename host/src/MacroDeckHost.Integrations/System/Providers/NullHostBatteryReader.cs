namespace MacroDeckHost.Integrations.System.Providers;

internal sealed class NullHostBatteryReader : IHostBatteryReader
{
	public Task<IReadOnlyList<HostBatteryReading>?> ReadAsync(CancellationToken cancellationToken)
	{
		return Task.FromResult<IReadOnlyList<HostBatteryReading>?>([]);
	}
}
