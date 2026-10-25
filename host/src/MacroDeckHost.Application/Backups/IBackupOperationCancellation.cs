namespace MacroDeckHost.Application.Backups;

public interface IBackupCancellationScope : IDisposable
{
	CancellationToken Token { get; }

	bool IsCancelled { get; }
}

public interface IBackupOperationCancellation
{
	IBackupCancellationScope Begin(Guid operationId, CancellationToken cancellationToken);

	bool Cancel(Guid operationId);
}
