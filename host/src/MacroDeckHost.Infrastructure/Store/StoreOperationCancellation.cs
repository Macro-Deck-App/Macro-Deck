using System.Collections.Concurrent;

namespace MacroDeckHost.Infrastructure.Store;

/// <summary>Lets <c>IStoreInstallCoordinator.Cancel</c> interrupt an operation that is already running on
/// the single worker thread, without either side needing to know about the other's internals. A queued
/// operation that has not started yet has no entry here - the coordinator cancels it directly instead.
/// </summary>
public sealed class StoreOperationCancellation
{
	private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _sources = new();

	public CancellationTokenSource Begin(Guid operationId, CancellationToken linkedToken)
	{
		var cts = CancellationTokenSource.CreateLinkedTokenSource(linkedToken);
		_sources[operationId] = cts;
		return cts;
	}

	public void End(Guid operationId)
	{
		if (_sources.TryRemove(operationId, out var cts))
		{
			cts.Dispose();
		}
	}

	public bool Cancel(Guid operationId)
	{
		if (!_sources.TryGetValue(operationId, out var cts))
		{
			return false;
		}

		try
		{
			cts.Cancel();
			return true;
		}
		catch (ObjectDisposedException)
		{
			return false;
		}
	}
}
