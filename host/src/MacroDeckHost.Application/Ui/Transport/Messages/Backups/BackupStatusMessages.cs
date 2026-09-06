namespace MacroDeckHost.Application.Ui.Transport.Messages.Backups;

public sealed class GetBackupStatusRequest;

public sealed class GetBackupStatusResponse
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
