namespace MacroDeckHost.Application.Ui.Transport.Messages.Backups;

public sealed class BackupOperationProgressEvent
{
	public Guid? OperationId { get; set; }

	public string Kind { get; set; } = string.Empty;

	public string Stage { get; set; } = string.Empty;

	public string Trigger { get; set; } = string.Empty;

	public int? PercentComplete { get; set; }

	public long? BytesProcessed { get; set; }

	public long? TotalBytes { get; set; }

	public Guid? BackupId { get; set; }

	public string? Error { get; set; }

	public string? ErrorMessage { get; set; }

	public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class BackupListChangedEvent
{
	public string Reason { get; set; } = string.Empty;
}

public sealed class BackupRecoveryKeyStateChangedEvent
{
	public string State { get; set; } = string.Empty;

	public string? KeyId { get; set; }

	public DateTimeOffset? ExportedAt { get; set; }
}

public sealed class RestorePendingEvent
{
	public bool Pending { get; set; }

	public Guid? RestoreId { get; set; }

	public Guid? BackupId { get; set; }

	public bool RestartSupported { get; set; }

	public List<string> Components { get; set; } = [];
}
