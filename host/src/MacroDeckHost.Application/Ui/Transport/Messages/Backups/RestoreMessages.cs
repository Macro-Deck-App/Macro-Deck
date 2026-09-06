namespace MacroDeckHost.Application.Ui.Transport.Messages.Backups;

public sealed class PrepareRestoreRequest
{
	public Guid BackupId { get; set; }

	public List<string> Components { get; set; } = [];

	public string? RecoveryKey { get; set; }

	public bool ProceedWithoutSafetyBackup { get; set; }
}

public sealed class PrepareRestoreResponse
{
	public bool Success { get; set; }

	public TransportError? Error { get; set; }

	public Guid RestoreId { get; set; }

	public Guid BackupId { get; set; }

	public List<string> Effective { get; set; } = [];

	public List<string> AutoSelected { get; set; } = [];

	public List<BackupDependencyWarningDto> Warnings { get; set; } = [];

	public bool RecoveryKeyRequired { get; set; }

	public Guid? SafetyBackupId { get; set; }

	public bool SafetyBackupCreated { get; set; }

	public List<BackupComponentCatalogEntry> Catalog { get; set; } = [];
}

public sealed class CommitRestoreRequest
{
	public Guid RestoreId { get; set; }
}

public sealed class CommitRestoreResponse
{
	public bool Success { get; set; }

	public TransportError? Error { get; set; }

	public bool RestartRequested { get; set; }

	public bool RestartSupported { get; set; }

	public string? RestartUnavailableReason { get; set; }
}

public sealed class CancelRestoreRequest
{
	public Guid RestoreId { get; set; }
}

public sealed class CancelRestoreResponse
{
	public bool Success { get; set; }

	public TransportError? Error { get; set; }
}
