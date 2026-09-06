using MacroDeckHost.Application.Backups;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Infrastructure.Backups;

public sealed class BackupOperationGate : IBackupOperationGate, IDisposable
{
	private readonly SemaphoreSlim _lease = new(1, 1);

	public bool RestorePending { get; set; }

	public bool IsBusy => _lease.CurrentCount == 0;

	public Result<IBackupOperationLease, BackupError> TryAcquire(BackupOperationKind kind, BackupTrigger trigger)
	{
		if (RestorePending && trigger != BackupTrigger.BeforeRestore)
		{
			return Result.Fail<IBackupOperationLease, BackupError>(BackupError.RestorePending,
				"A restore is waiting to be applied.");
		}

		if (!_lease.Wait(0))
		{
			return Result.Fail<IBackupOperationLease, BackupError>(BackupError.Busy,
				"Another backup operation is already running.");
		}

		return Result.Ok<IBackupOperationLease, BackupError>(new Lease(_lease));
	}

	public async Task<Result<IBackupOperationLease, BackupError>> Acquire(BackupOperationKind kind,
		BackupTrigger trigger,
		TimeSpan timeout,
		CancellationToken cancellationToken = default)
	{
		if (RestorePending && trigger != BackupTrigger.BeforeRestore)
		{
			return Result.Fail<IBackupOperationLease, BackupError>(BackupError.RestorePending,
				"A restore is waiting to be applied.");
		}

		if (!await _lease.WaitAsync(timeout, cancellationToken))
		{
			return Result.Fail<IBackupOperationLease, BackupError>(BackupError.Busy,
				"Another backup operation is already running.");
		}

		return Result.Ok<IBackupOperationLease, BackupError>(new Lease(_lease));
	}

	public void Dispose() => _lease.Dispose();

	private sealed class Lease : IBackupOperationLease
	{
		private readonly SemaphoreSlim _semaphore;
		private int _released;

		public Lease(SemaphoreSlim semaphore) => _semaphore = semaphore;

		public Guid OperationId { get; } = Guid.NewGuid();

		public void Dispose()
		{
			if (Interlocked.Exchange(ref _released, 1) == 0)
			{
				_semaphore.Release();
			}
		}
	}
}
