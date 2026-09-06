using System.Collections.Concurrent;
using MacroDeckHost.Domain.Icons;

namespace MacroDeckHost.Application.Icons;

public sealed class IconImportCoalescer
{
	public static readonly Guid CatalogWideScope = Guid.Empty;

	private readonly ConcurrentDictionary<(Guid Scope, string Hash), Guid> _inFlight = new();

	public Guid? TryReserve(Guid scope, SourceContentHash hash, Guid iconId)
	{
		var winner = _inFlight.GetOrAdd((scope, hash.Value), iconId);
		return winner == iconId ? null : winner;
	}

	public void ReleaseAll(Guid iconId)
	{
		foreach (var entry in _inFlight.Where(entry => entry.Value == iconId).ToList())
		{
			_inFlight.TryRemove(entry);
		}
	}
}
