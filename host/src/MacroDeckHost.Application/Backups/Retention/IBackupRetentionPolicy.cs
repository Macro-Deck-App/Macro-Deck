using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Backups.Retention;

public sealed record BackupRetentionSettings(string PolicyId, int KeepLatest);

public interface IBackupRetentionPolicy
{
	string PolicyId { get; }

	IReadOnlyList<BackupDescriptor> SelectForDeletion(
		IReadOnlyList<BackupDescriptor> backups,
		BackupRetentionSettings settings);
}

public interface IBackupRetentionService
{
	Task Apply(CancellationToken cancellationToken = default);
}

public static class BackupRetentionRules
{
	/// <summary>
	/// Backups retention must never remove. An imported archive may be the only copy the user has, and a
	/// safety backup taken before a restore is the only way back from it.
	/// </summary>
	public static bool IsExempt(BackupDescriptor backup)
		=> backup.Imported || backup.Trigger == BackupTrigger.BeforeRestore;
}
