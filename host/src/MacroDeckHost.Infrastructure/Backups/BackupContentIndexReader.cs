using System.IO.Compression;
using System.Text.Json;
using MacroDeckHost.Application.Backups;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.Persistence;

namespace MacroDeckHost.Infrastructure.Backups;

internal static class BackupContentIndexReader
{
	public static async Task<Result<BackupContentIndex, BackupError>> Read(
		ZipArchive payload,
		CancellationToken cancellationToken)
	{
		var entry = payload.GetEntry(BackupFileNames.ContentIndexEntry);
		if (entry is null)
		{
			return Result.Fail<BackupContentIndex, BackupError>(BackupError.InvalidArchive,
				"The archive content index is missing.");
		}

		if (entry.Length > BackupArchiveLimits.MaxContentIndexBytes)
		{
			return Result.Fail<BackupContentIndex, BackupError>(BackupError.TooLarge,
				"The archive content index is too large.");
		}

		using var buffer = new MemoryStream();
		await using (var source = entry.Open())
		{
			if (!await CopyBounded(source, buffer, BackupArchiveLimits.MaxContentIndexBytes, cancellationToken))
			{
				return Result.Fail<BackupContentIndex, BackupError>(BackupError.TooLarge,
					"The archive content index is too large.");
			}
		}

		buffer.Position = 0;

		BackupContentIndex? index;
		try
		{
			index = JsonSerializer.Deserialize<BackupContentIndex>(buffer, PersistenceJsonOptions.Default);
		}
		catch (JsonException)
		{
			index = null;
		}

		if (index?.Entries is null
			|| index.Components is null
			|| index.Entries.Any(item => item is null)
			|| index.Components.Any(item => item is null))
		{
			return Result.Fail<BackupContentIndex, BackupError>(BackupError.InvalidArchive,
				"The archive content index could not be read.");
		}

		return Result.Ok<BackupContentIndex, BackupError>(index);
	}

	public static async Task<bool> CopyBounded(
		Stream source,
		Stream destination,
		long limit,
		CancellationToken cancellationToken)
	{
		var buffer = new byte[81_920];
		var total = 0L;

		while (true)
		{
			var read = await source.ReadAsync(buffer, cancellationToken);
			if (read == 0)
			{
				return true;
			}

			total += read;
			if (total > limit)
			{
				return false;
			}

			await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
		}
	}
}
