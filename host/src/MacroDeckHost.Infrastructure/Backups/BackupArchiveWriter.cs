using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using MacroDeckHost.Application.Backups;
using MacroDeckHost.Infrastructure.Backups.Crypto;
using MacroDeckHost.Infrastructure.Persistence;

namespace MacroDeckHost.Infrastructure.Backups;

public sealed class BackupArchiveWriter : IBackupArchiveWriter
{
	private static readonly JsonSerializerOptions _json = PersistenceJsonOptions.Default;

	public async Task<BackupContentIndex> Write(Stream destination,
		BackupArchiveWriteRequest request,
		ReadOnlyMemory<byte> recoveryKey,
		CancellationToken cancellationToken = default)
	{
		var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(request.Manifest, _json);
		var index = new BackupContentIndex { Skipped = [.. request.Plan.Skipped] };

		using var outer = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);

		// The manifest is written before the payload and its exact bytes are the associated data of the
		// key wrap, so any later edit to it breaks decryption instead of silently changing what the
		// archive claims to be.
		var manifestEntry = outer.CreateEntry(BackupFileNames.ManifestEntry, CompressionLevel.Optimal);
		await using (var manifestStream = manifestEntry.Open())
		{
			await manifestStream.WriteAsync(manifestBytes, cancellationToken);
		}

		var payloadEntry = outer.CreateEntry(BackupFileNames.PayloadEntry, CompressionLevel.NoCompression);
		await using var payloadStream = payloadEntry.Open();
		await using var encryptor = BackupPayloadCrypto.CreateEncryptor(payloadStream,
			recoveryKey.Span,
			manifestBytes,
			leaveOpen: true);

		using (var inner = new ZipArchive(encryptor, ZipArchiveMode.Create, leaveOpen: true))
		{
			await WriteEntry(inner, BackupFileNames.ManifestEntry, manifestBytes, cancellationToken);

			index.Database = await WriteDatabase(inner, request, cancellationToken);

			foreach (var file in request.Plan.Files)
			{
				cancellationToken.ThrowIfCancellationRequested();

				var entry = await WriteFile(inner, file, request.BytesProcessed, cancellationToken);
				if (entry is null)
				{
					index.Skipped.Add(new BackupSkippedEntry { Path = file.RelativePath, Reason = "unreadable" });
					continue;
				}

				index.Entries.Add(entry);
			}

			index.Components =
			[
				.. index.Entries
					.GroupBy(entry => entry.Component)
					.Select(group => new BackupComponentEntry
					{
						Id = group.Key,
						EntryCount = group.Count(),
						ByteSize = group.Sum(entry => entry.Size),
						Tables = [.. BackupComponentGroups.Definition(group.Key).Tables]
					})
			];

			// Written last so it can describe every entry that precedes it. Readers look it up by name,
			// not by position.
			await WriteEntry(inner,
				BackupFileNames.ContentIndexEntry,
				JsonSerializer.SerializeToUtf8Bytes(index, _json),
				cancellationToken);
		}

		encryptor.Finish();

		return index;
	}

	private static async Task<BackupDatabaseInfo> WriteDatabase(ZipArchive inner,
		BackupArchiveWriteRequest request,
		CancellationToken cancellationToken)
	{
		var entry = inner.CreateEntry(BackupFileNames.DatabaseEntry, CompressionLevel.Optimal);

		FileStream opened;
		try
		{
			opened = File.OpenRead(request.DatabaseCopyPath);
		}
		catch (IOException e) when (BackupFileLockedException.IsLockViolation(e))
		{
			throw new BackupFileLockedException(request.DatabaseCopyPath, e);
		}

		await using var source = opened;
		await using var target = entry.Open();

		var digest = await CopyWithDigest(source, target, request.BytesProcessed, cancellationToken);

		return new BackupDatabaseInfo
		{
			Size = source.Length,
			Sha256 = digest,
			SchemaVersion = request.Manifest.DatabaseSchemaVersion
		};
	}

	private static async Task<BackupContentEntry?> WriteFile(ZipArchive inner,
		BackupSnapshotFile file,
		IProgress<long>? progress,
		CancellationToken cancellationToken)
	{
		FileStream source;
		try
		{
			source = File.OpenRead(file.AbsolutePath);
		}
		catch (IOException)
		{
			return null;
		}
		catch (UnauthorizedAccessException)
		{
			return null;
		}

		await using (source)
		{
			var entry = inner.CreateEntry(BackupFileNames.FilesPrefix + file.RelativePath, CompressionLevel.Optimal);
			await using var target = entry.Open();
			var digest = await CopyWithDigest(source, target, progress, cancellationToken);

			return new BackupContentEntry
			{
				Path = file.RelativePath,
				Size = source.Length,
				Sha256 = digest,
				Component = file.Component
			};
		}
	}

	private static async Task WriteEntry(ZipArchive archive,
		string name,
		byte[] content,
		CancellationToken cancellationToken)
	{
		var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
		await using var stream = entry.Open();
		await stream.WriteAsync(content, cancellationToken);
	}

	private static async Task<string> CopyWithDigest(Stream source,
		Stream destination,
		IProgress<long>? progress,
		CancellationToken cancellationToken)
	{
		using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
		var buffer = new byte[81_920];

		while (true)
		{
			var read = await source.ReadAsync(buffer, cancellationToken);
			if (read == 0)
			{
				break;
			}

			hash.AppendData(buffer, 0, read);
			await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
			progress?.Report(read);
		}

		return Convert.ToHexStringLower(hash.GetHashAndReset());
	}
}
