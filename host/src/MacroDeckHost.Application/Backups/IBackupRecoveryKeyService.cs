using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Backups;

public enum BackupRecoveryKeyAvailability
{
	None,
	Available,
	Missing
}

public sealed record BackupRecoveryKeyState(
	BackupRecoveryKeyAvailability Availability,
	string? KeyId,
	DateTimeOffset? CreatedAt,
	DateTimeOffset? ExportedAt);

public interface IBackupRecoveryKeyService
{
	Task<BackupRecoveryKeyState> GetState(CancellationToken cancellationToken = default);

	Task<Result<byte[], BackupError>> EnsureCreated(CancellationToken cancellationToken = default);

	Task<Result<byte[], BackupError>> Resolve(CancellationToken cancellationToken = default);

	Task<Result<string, BackupError>> Export(CancellationToken cancellationToken = default);

	Task<Result<BackupError>> Acknowledge(CancellationToken cancellationToken = default);

	Task<Result<string, BackupError>> Regenerate(CancellationToken cancellationToken = default);

	bool TryParseExportedKey(string? text, out byte[] key);
}
