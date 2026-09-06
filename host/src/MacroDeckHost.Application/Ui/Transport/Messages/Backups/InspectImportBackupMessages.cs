namespace MacroDeckHost.Application.Ui.Transport.Messages.Backups;

public sealed class InspectBackupRequest
{
	public Guid BackupId { get; set; }

	public string? RecoveryKey { get; set; }
}

public sealed class InspectBackupPathRequest
{
	public string Path { get; set; } = string.Empty;

	public string? RecoveryKey { get; set; }
}

public sealed class InspectBackupResponse
{
	public bool Success { get; set; }

	public TransportError? Error { get; set; }

	public BackupSummary? Backup { get; set; }

	public bool RecoveryKeyRequired { get; set; }

	public List<BackupComponentGroupInfoDto> Components { get; set; } = [];

	public List<BackupComponentCatalogEntry> Catalog { get; set; } = [];
}

public sealed class ImportBackupPathRequest
{
	public string Path { get; set; } = string.Empty;
}

public sealed class ImportBackupResponse
{
	public bool Success { get; set; }

	public TransportError? Error { get; set; }

	public BackupSummary? Backup { get; set; }
}
