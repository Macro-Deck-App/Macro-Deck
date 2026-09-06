namespace MacroDeckHost.Application.Ui.Transport.Messages.Backups;

public sealed class CreateBackupRequest
{
	public string? Note { get; set; }

	public bool Protected { get; set; }
}

public sealed class CreateBackupResponse
{
	public bool Success { get; set; }

	public TransportError? Error { get; set; }

	public BackupSummary? Backup { get; set; }
}

public sealed class DeleteBackupRequest
{
	public Guid BackupId { get; set; }
}

public sealed class DeleteBackupResponse
{
	public bool Success { get; set; }

	public TransportError? Error { get; set; }
}
