namespace MacroDeckHost.Application.Ui.Transport.Messages.Backups;

public sealed class GetBackupsRequest;

public sealed class GetBackupsResponse
{
	public List<BackupSummary> Backups { get; set; } = [];

	public int RetentionKeepLatest { get; set; }

	public PendingRestoreSummary? PendingRestore { get; set; }
}
