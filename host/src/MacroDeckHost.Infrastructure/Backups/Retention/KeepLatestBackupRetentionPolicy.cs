using MacroDeckHost.Application.Backups;
using MacroDeckHost.Application.Backups.Retention;

namespace MacroDeckHost.Infrastructure.Backups.Retention;

public sealed class KeepLatestBackupRetentionPolicy : IBackupRetentionPolicy
{
	public const string Id = "keep-latest";

	public string PolicyId => Id;

	public IReadOnlyList<BackupDescriptor> SelectForDeletion(
		IReadOnlyList<BackupDescriptor> backups,
		BackupRetentionSettings settings)
		=>
		[
			.. backups
				.Where(backup => !BackupRetentionRules.IsExempt(backup))
				.OrderByDescending(backup => backup.CreatedAt)
				.Skip(Math.Max(settings.KeepLatest, 0))
		];
}
