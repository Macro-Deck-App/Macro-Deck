using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Backups;

public sealed record PrepareRestoreRequest(
	Guid BackupId,
	IReadOnlyList<BackupComponentGroup> Components,
	string? RecoveryKey);

public sealed record PreparedRestore(
	Guid RestoreId,
	Guid BackupId,
	IReadOnlyList<BackupComponentGroup> Effective,
	IReadOnlyList<BackupComponentGroup> AutoSelected,
	IReadOnlyList<BackupDependencyWarning> Warnings,
	Guid? SafetyBackupId,
	bool SafetyBackupCreated);

public sealed record PendingRestoreInfo(
	Guid RestoreId,
	Guid BackupId,
	DateTimeOffset StagedAt,
	IReadOnlyList<BackupComponentGroup> Components);

public sealed record CommitRestoreResult(
	bool RestartRequested,
	bool RestartSupported,
	string? RestartUnavailableReason);

public interface IRestoreService
{
	PendingRestoreInfo? Pending { get; }

	Task<Result<PreparedRestore, BackupError>> Prepare(PrepareRestoreRequest request,
		bool proceedWithoutSafetyBackup = false,
		CancellationToken cancellationToken = default);

	Task<Result<CommitRestoreResult, BackupError>> Commit(Guid restoreId,
		CancellationToken cancellationToken = default);

	Task<Result<BackupError>> Cancel(Guid restoreId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Narrow port so the plugin installer can refuse to mutate data a staged restore is about to replace,
/// without taking a dependency on the whole backup service.
/// </summary>
public interface IRestoreLock
{
	bool IsRestorePending { get; }
}

public sealed record PreUpdateBackupOutcome(bool Success, bool Skipped, string? Reason, Guid? BackupId);

public interface IPreUpdateBackupCoordinator : IRestoreLock
{
	Task<PreUpdateBackupOutcome> EnsureBeforePluginUpdate(string pluginId,
		string? batchId,
		CancellationToken cancellationToken = default);

	Task<PreUpdateBackupOutcome> CreateBeforeHostUpdate(string? version,
		CancellationToken cancellationToken = default);
}
