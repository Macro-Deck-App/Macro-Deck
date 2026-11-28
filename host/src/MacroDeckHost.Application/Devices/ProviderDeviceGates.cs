using System.Collections.Concurrent;

namespace MacroDeckHost.Application.Devices;

// One instance per host: every read-modify-write of a provider's device rows runs under its gate, or a
// full-entity save from one caller overwrites what another just wrote.
public sealed class ProviderDeviceGates
{
	private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new(StringComparer.Ordinal);

	public async Task<T> Serialized<T>(
		string providerId,
		Func<Task<T>> operation,
		CancellationToken cancellationToken)
	{
		var gate = _gates.GetOrAdd(providerId, static _ => new SemaphoreSlim(1, 1));
		await gate.WaitAsync(cancellationToken);
		try
		{
			return await operation();
		}
		finally
		{
			gate.Release();
		}
	}
}
