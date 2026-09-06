using System.Collections.Concurrent;

namespace MacroDeckHost.Application.Icons;

public sealed class IconImportCancellationRegistry
{
	private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _sources = new();

	public CancellationToken TokenFor(Guid batchId)
		=> _sources.GetOrAdd(batchId, static _ => new CancellationTokenSource()).Token;

	public bool Cancel(Guid batchId)
	{
		var source = _sources.GetOrAdd(batchId, static _ => new CancellationTokenSource());
		try
		{
			source.Cancel();
		}
		catch (ObjectDisposedException)
		{
			return false;
		}

		return true;
	}

	public void Release(Guid batchId)
	{
		if (_sources.TryRemove(batchId, out var source))
		{
			source.Dispose();
		}
	}
}
