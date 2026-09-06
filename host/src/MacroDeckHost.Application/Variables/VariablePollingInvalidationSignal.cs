using System.Collections.Concurrent;

namespace MacroDeckHost.Application.Variables;

public sealed class VariablePollingInvalidationSignal : IVariablePollingInvalidationSignal
{
	private readonly ConcurrentDictionary<string, byte> _stale = new(StringComparer.Ordinal);

	public void MarkStale(string integrationId) => _stale.TryAdd(integrationId, 0);

	public IReadOnlyCollection<string> DrainStale()
	{
		if (_stale.IsEmpty)
		{
			return [];
		}

		var ids = _stale.Keys.ToList();
		foreach (var id in ids)
		{
			_stale.TryRemove(id, out _);
		}

		return ids;
	}
}
