using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Backups.Storage;

public sealed record BackupStorageCapabilities(
	bool IsRemote,
	bool SupportsDelete,
	long? MaxObjectBytes);

public sealed record BackupStorageAvailability(bool Available, string? Reason);

public sealed record BackupStorageObject(
	string ProviderId,
	string StorageId,
	string Name,
	long SizeBytes,
	DateTimeOffset CreatedAt);

public sealed record BackupTransferProgress(long BytesTransferred, long? TotalBytes);

/// <summary>
/// Content is supplied as a callback rather than a stream so a provider can invoke it again for a retry
/// or a resumed upload, and <see cref="BackupStorageWriteRequest.ExpectedLength"/> is required because an
/// object store needs the length up front. Together they force the caller to hand over a finished,
/// already encrypted container: a provider never sees anything but ciphertext.
/// </summary>
public sealed record BackupStorageWriteRequest(
	string SuggestedName,
	long ExpectedLength,
	Func<Stream, CancellationToken, Task> WriteContent,
	string? StagedFilePath = null,
	IProgress<BackupTransferProgress>? Progress = null);

public interface IBackupStorageProvider
{
	string ProviderId { get; }

	string DisplayName { get; }

	BackupStorageCapabilities Capabilities { get; }

	ValueTask<BackupStorageAvailability> GetAvailability(CancellationToken cancellationToken = default);

	IAsyncEnumerable<BackupStorageObject> List(CancellationToken cancellationToken = default);

	Task<Result<Stream, BackupError>> OpenRead(string storageId, CancellationToken cancellationToken = default);

	Task<Result<BackupStorageObject, BackupError>> Write(BackupStorageWriteRequest request,
		CancellationToken cancellationToken = default);

	Task<Result<BackupError>> Delete(string storageId, CancellationToken cancellationToken = default);
}

public interface IBackupStorageRegistry
{
	IReadOnlyList<IBackupStorageProvider> Providers { get; }

	IBackupStorageProvider? Find(string providerId);

	IBackupStorageProvider Primary { get; }
}
