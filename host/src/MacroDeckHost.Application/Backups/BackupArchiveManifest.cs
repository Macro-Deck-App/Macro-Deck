using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Backups;

public sealed class BackupArchiveManifest
{
	// Additive only. New optional fields are tolerated by older readers, so bump this only for a
	// change an older Macro Deck cannot read at all - a reader rejects anything newer outright.
	public const int CurrentFormatVersion = 1;

	public int FormatVersion { get; set; } = CurrentFormatVersion;

	public Guid BackupId { get; set; }

	public string MacroDeckVersion { get; set; } = string.Empty;

	public DateTimeOffset CreatedAt { get; set; }

	public BackupTrigger Trigger { get; set; }

	public Guid? InstallationId { get; set; }

	/// <summary>
	/// One-way fingerprint of the recovery key that encrypted this archive. Restore compares it to the
	/// local key before decrypting, so a foreign backup asks for its key instead of failing a decrypt.
	/// </summary>
	public string RecoveryKeyId { get; set; } = string.Empty;

	public string? Note { get; set; }

	public string HostPlatform { get; set; } = string.Empty;

	public string DatabaseSchemaVersion { get; set; } = string.Empty;

	public List<BackupComponentGroup> Components { get; set; } = [];

	public BackupEncryptionInfo Encryption { get; set; } = new();
}

public sealed class BackupEncryptionInfo
{
	public const string Aes256GcmStream = "AES-256-GCM-STREAM";
	public const string HkdfSha256 = "HKDF-SHA256";

	public int Version { get; set; } = 1;

	public string Cipher { get; set; } = Aes256GcmStream;

	public string Kdf { get; set; } = HkdfSha256;
}
