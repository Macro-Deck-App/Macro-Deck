using System.IO.Compression;
using System.Text.Json;
using MacroDeckHost.Application.Backups;
using MacroDeckHost.Infrastructure.Backups.Crypto;
using MacroDeckHost.Infrastructure.Persistence;

namespace MacroDeckHost.Infrastructure.Backups;

public sealed class BackupArchiveReader : IBackupArchiveReader
{
	private static readonly JsonSerializerOptions _json = PersistenceJsonOptions.Default;

	public BackupArchiveManifest? ReadManifest(Stream archive)
	{
		using var outer = new ZipArchive(archive, ZipArchiveMode.Read, leaveOpen: true);

		if (outer.Entries.Count > BackupArchiveLimits.MaxOuterEntries)
		{
			return null;
		}

		var entry = outer.GetEntry(BackupFileNames.ManifestEntry);
		if (entry is null || entry.Length > BackupArchiveLimits.MaxManifestBytes)
		{
			return null;
		}

		using var stream = entry.Open();

		try
		{
			return JsonSerializer.Deserialize<BackupArchiveManifest>(stream, _json);
		}
		catch (JsonException)
		{
			return null;
		}
	}

	public async Task DecryptPayload(Stream archive,
		ReadOnlyMemory<byte> recoveryKey,
		string destinationPath,
		IProgress<long>? bytesProcessed = null,
		CancellationToken cancellationToken = default)
	{
		using var outer = new ZipArchive(archive, ZipArchiveMode.Read, leaveOpen: true);

		var manifestEntry = outer.GetEntry(BackupFileNames.ManifestEntry) ??
			throw new BackupCryptoException(BackupDecryptResult.Corrupt, "The archive has no manifest.");
		var payloadEntry = outer.GetEntry(BackupFileNames.PayloadEntry) ??
			throw new BackupCryptoException(BackupDecryptResult.Corrupt, "The archive has no payload.");

		if (payloadEntry.Length > BackupArchiveLimits.MaxPayloadBytes)
		{
			throw new BackupCryptoException(BackupDecryptResult.TooLarge, "The archive payload is too large.");
		}

		byte[] manifestBytes;
		await using (var manifestStream = manifestEntry.Open())
		{
			using var buffer = new MemoryStream();
			await manifestStream.CopyToAsync(buffer, cancellationToken);
			manifestBytes = buffer.ToArray();
		}

		await using var payloadStream = payloadEntry.Open();
		await using var decryptor = BackupPayloadCrypto.CreateDecryptor(payloadStream,
			recoveryKey.Span,
			manifestBytes,
			leaveOpen: true);
		await using var destination = new FileStream(destinationPath,
			FileMode.Create,
			FileAccess.Write,
			FileShare.None);

		var chunk = new byte[81_920];
		var total = 0L;

		while (true)
		{
			var read = await decryptor.ReadAsync(chunk, cancellationToken);
			if (read == 0)
			{
				break;
			}

			total += read;
			if (total > BackupArchiveLimits.MaxTotalPlaintextBytes)
			{
				throw new BackupCryptoException(BackupDecryptResult.TooLarge,
					"The archive expands beyond the supported size.");
			}

			await destination.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
			bytesProcessed?.Report(read);
		}

		await destination.FlushAsync(cancellationToken);
	}
}
