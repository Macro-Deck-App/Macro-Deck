using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Backups;

public sealed record BackupDescriptor(
	Guid BackupId,
	string ProviderId,
	string StorageId,
	string Name,
	DateTimeOffset CreatedAt,
	BackupTrigger Trigger,
	string MacroDeckVersion,
	int FormatVersion,
	int EncryptionVersion,
	long SizeBytes,
	bool IsRemote,
	bool DecryptableLocally,
	bool Imported,
	string? Note,
	IReadOnlyList<BackupComponentGroup> Components);

public sealed record BackupInspection(
	BackupDescriptor Backup,
	bool RecoveryKeyRequired,
	IReadOnlyList<BackupComponentGroupInfo> Components);

public sealed record BackupComponentGroupInfo(
	BackupComponentGroup Id,
	int EntryCount,
	long ByteSize,
	IReadOnlyList<BackupComponentGroup> Requires);
