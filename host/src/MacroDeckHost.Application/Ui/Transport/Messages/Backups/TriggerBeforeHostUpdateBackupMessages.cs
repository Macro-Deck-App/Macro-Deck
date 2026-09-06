namespace MacroDeckHost.Application.Ui.Transport.Messages.Backups;

public sealed class TriggerBeforeHostUpdateBackupRequest
{
	public string? Version { get; set; }
}

public sealed class TriggerBeforeHostUpdateBackupResponse
{
	public bool Success { get; set; }

	public bool Skipped { get; set; }

	public string? Reason { get; set; }

	public Guid? BackupId { get; set; }
}
