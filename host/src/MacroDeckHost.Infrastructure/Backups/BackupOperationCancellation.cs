using System.Collections.Concurrent;
using MacroDeckHost.Application.Backups;

namespace MacroDeckHost.Infrastructure.Backups;

public sealed class BackupOperationCancellation : IBackupOperationCancellation
{
	private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _running = new();

	public IBackupCancellationScope Begin(Guid operationId, CancellationToken cancellationToken)
	{
		var source = new CancellationTokenSource();
		_running[operationId] = source;

		return new Scope(this, operationId, source, cancellationToken);
	}

	public bool Cancel(Guid operationId)
	{
		if (!_running.TryGetValue(operationId, out var source))
		{
			return false;
		}

		try
		{
			source.Cancel();
			return true;
		}
		catch (ObjectDisposedException)
		{
			return false;
		}
	}

	private sealed class Scope : IBackupCancellationScope
	{
		private readonly BackupOperationCancellation _owner;
		private readonly Guid _operationId;
		private readonly CancellationTokenSource _own;
		private readonly CancellationTokenSource _linked;

		public Scope(
			BackupOperationCancellation owner,
			Guid operationId,
			CancellationTokenSource own,
			CancellationToken outer)
		{
			_owner = owner;
			_operationId = operationId;
			_own = own;
			_linked = CancellationTokenSource.CreateLinkedTokenSource(own.Token, outer);
		}

		public CancellationToken Token => _linked.Token;

		public bool IsCancelled => _own.IsCancellationRequested;

		public void Dispose()
		{
			_owner._running.TryRemove(new KeyValuePair<Guid, CancellationTokenSource>(_operationId, _own));
			_linked.Dispose();
			_own.Dispose();
		}
	}
}
