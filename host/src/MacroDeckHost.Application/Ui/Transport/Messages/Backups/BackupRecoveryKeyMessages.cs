namespace MacroDeckHost.Application.Ui.Transport.Messages.Backups;

public sealed class GetBackupRecoveryKeyStateRequest;

public sealed class GetBackupRecoveryKeyStateResponse
{
	public string Availability { get; set; } = string.Empty;

	public string? KeyId { get; set; }

	public DateTimeOffset? CreatedAt { get; set; }

	public DateTimeOffset? ExportedAt { get; set; }
}

public sealed class CreateBackupRecoveryKeyRequest;

public sealed class AcknowledgeBackupRecoveryKeyRequest;

public sealed class RevealBackupRecoveryKeyRequest
{
	public bool Confirm { get; set; }
}

public sealed class RegenerateBackupRecoveryKeyRequest
{
	public bool Confirm { get; set; }

	/// <summary>
	/// Separate from <see cref="Confirm"/> on purpose. Rotating the key leaves every existing backup
	/// readable only with the previously exported key, so the acknowledgement is enforced here rather than
	/// left to whichever client happens to be asking.
	/// </summary>
	public bool AcknowledgeExistingBackupsBecomeUnreadable { get; set; }
}

public sealed class BackupRecoveryKeyResponse
{
	public bool Success { get; set; }

	public TransportError? Error { get; set; }

	public string Availability { get; set; } = string.Empty;

	public string? KeyId { get; set; }

	public DateTimeOffset? CreatedAt { get; set; }

	public DateTimeOffset? ExportedAt { get; set; }

	public string? Key { get; set; }
}
