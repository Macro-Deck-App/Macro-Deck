using System.Collections.Concurrent;

namespace MacroDeckHost.Infrastructure.Store;

public sealed class StoreInstallBackupBatches
{
	private readonly ConcurrentDictionary<Guid, string> _batches = new();

	public void Record(Guid operationId, string batchId) => _batches[operationId] = batchId;

	public string? Consume(Guid operationId) => _batches.TryRemove(operationId, out var batchId) ? batchId : null;
}
