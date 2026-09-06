namespace MacroDeckHost.Application.Backups;

public sealed record BackupArchiveWriteRequest(
	BackupArchiveManifest Manifest,
	BackupSnapshotPlan Plan,
	string DatabaseCopyPath,
	IProgress<long>? BytesProcessed = null);

public interface IBackupArchiveWriter
{
	Task<BackupContentIndex> Write(Stream destination,
		BackupArchiveWriteRequest request,
		ReadOnlyMemory<byte> recoveryKey,
		CancellationToken cancellationToken = default);
}

public interface IBackupArchiveReader
{
	/// <summary>
	/// Reads the plaintext manifest without any key material, so a foreign archive can be listed and
	/// described before the user is asked for its recovery key.
	/// </summary>
	BackupArchiveManifest? ReadManifest(Stream archive);

	/// <summary>
	/// Decrypts and authenticates the whole payload into <paramref name="destinationPath"/>, verifying
	/// every segment. Throws <c>BackupCryptoException</c> for an archive that fails authentication.
	/// </summary>
	Task DecryptPayload(Stream archive,
		ReadOnlyMemory<byte> recoveryKey,
		string destinationPath,
		IProgress<long>? bytesProcessed = null,
		CancellationToken cancellationToken = default);
}
