using System.Buffers;
using MacroDeckHost.Application.Icons;
using MacroDeckHost.Application.Paths;
using MacroDeckHost.Domain.Icons;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.Icons;

public sealed class FileSystemIconStorage : IIconStorage
{
	private const string ArchiveDirectoryName = "_archive";
	private const string IconsDirectoryName = "icons";

	private readonly IMacroDeckPaths _paths;
	private readonly ILogger _logger;

	public FileSystemIconStorage(IMacroDeckPaths paths, ILogger logger)
	{
		_paths = paths;
		_logger = logger;
	}

	public async Task<SourceContentHash> StageOriginal(Guid batchId,
		Guid iconId,
		string originalFileName,
		Stream content,
		CancellationToken cancellationToken)
	{
		var directory = StagingDirectoryFor(batchId);
		Directory.CreateDirectory(directory);

		var extension = SanitizeExtension(Path.GetExtension(originalFileName));
		var path = Path.Combine(directory, iconId + extension);
		return SourceContentHash.FromComputed(await WriteAtomic(path, content, cancellationToken));
	}

	public async Task<string> StageArchive(Guid batchId,
		string fileName,
		Stream content,
		CancellationToken cancellationToken)
	{
		var directory = Path.Combine(StagingDirectoryFor(batchId), ArchiveDirectoryName);
		Directory.CreateDirectory(directory);

		var name = $"{Guid.CreateVersion7():N}_{SanitizeFileName(fileName)}";
		var path = Path.Combine(directory, name);
		await WriteAtomic(path, content, cancellationToken);
		return path;
	}

	public Stream? OpenStagedOriginal(Guid batchId, Guid iconId)
	{
		var directory = StagingDirectoryFor(batchId);
		if (!Directory.Exists(directory))
		{
			return null;
		}

		var path = Directory.EnumerateFiles(directory, iconId + ".*").FirstOrDefault();
		return path is null ? null : OpenRead(path);
	}

	public void DeleteStagedOriginal(Guid batchId, Guid iconId)
	{
		var directory = StagingDirectoryFor(batchId);
		if (!Directory.Exists(directory))
		{
			return;
		}

		foreach (var path in Directory.EnumerateFiles(directory, iconId + ".*"))
		{
			try
			{
				File.Delete(path);
			}
			catch (Exception ex)
			{
				_logger.Warning(ex, "Failed to delete staged original {Path}", path);
			}
		}
	}

	public IReadOnlyList<string> GetStagedArchivePaths(Guid batchId)
	{
		var directory = Path.Combine(StagingDirectoryFor(batchId), ArchiveDirectoryName);
		if (!Directory.Exists(directory))
		{
			return [];
		}

		return Directory.EnumerateFiles(directory).Order().ToList();
	}

	public async Task WriteVariant(Guid packId,
		Guid iconId,
		string variant,
		ReadOnlyMemory<byte> webpData,
		CancellationToken cancellationToken)
	{
		var directory = IconDirectoryFor(packId, iconId);
		Directory.CreateDirectory(directory);

		var path = Path.Combine(directory, variant + ".webp");
		var tempPath = path + ".tmp";
		await File.WriteAllBytesAsync(tempPath, webpData, cancellationToken);
		File.Move(tempPath, path, overwrite: true);
	}

	public async Task<string> WriteVariant(Guid packId,
		Guid iconId,
		string variant,
		Stream content,
		CancellationToken cancellationToken)
	{
		var directory = IconDirectoryFor(packId, iconId);
		Directory.CreateDirectory(directory);
		return await WriteAtomic(Path.Combine(directory, variant + ".webp"), content, cancellationToken);
	}

	public Stream? OpenVariant(Guid packId, Guid iconId, string variant)
	{
		var path = Path.Combine(IconDirectoryFor(packId, iconId), variant + ".webp");
		return File.Exists(path) ? OpenRead(path) : null;
	}

	public void DeleteIconFiles(Guid packId, Guid iconId)
	{
		try
		{
			var directory = IconDirectoryFor(packId, iconId);
			if (Directory.Exists(directory))
			{
				Directory.Delete(directory, recursive: true);
			}
		}
		catch (Exception ex)
		{
			_logger.Warning(ex, "Failed to delete icon files for {IconId} in pack {PackId}", iconId, packId);
		}
	}

	public void CleanupBatchStaging(Guid batchId)
	{
		try
		{
			var directory = StagingDirectoryFor(batchId);
			if (Directory.Exists(directory))
			{
				Directory.Delete(directory, recursive: true);
			}
		}
		catch (Exception ex)
		{
			_logger.Warning(ex, "Failed to clean up staging directory for batch {BatchId}", batchId);
		}
	}

	public IReadOnlyList<Guid> EnumerateStagedBatchIds()
	{
		if (!Directory.Exists(_paths.IconStagingDirectory))
		{
			return [];
		}

		var batchIds = new List<Guid>();
		foreach (var directory in Directory.EnumerateDirectories(_paths.IconStagingDirectory))
		{
			if (Guid.TryParse(Path.GetFileName(directory), out var batchId))
			{
				batchIds.Add(batchId);
			}
		}

		return batchIds;
	}

	private string StagingDirectoryFor(Guid batchId) => Path.Combine(_paths.IconStagingDirectory, batchId.ToString());

	private string IconDirectoryFor(Guid packId, Guid iconId)
		=> Path.Combine(_paths.IconPacksDirectory, packId.ToString(), IconsDirectoryName, iconId.ToString());

	private static async Task<string> WriteAtomic(string path, Stream content, CancellationToken cancellationToken)
	{
		var tempPath = path + ".tmp";
		using var hash = ContentHash.CreateIncremental();
		var buffer = ArrayPool<byte>.Shared.Rent(81920);
		try
		{
			await using (var file = File.Create(tempPath))
			{
				int read;
				while ((read = await content.ReadAsync(buffer, cancellationToken)) > 0)
				{
					hash.Append(buffer.AsSpan(0, read));
					await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
				}
			}
		}
		finally
		{
			ArrayPool<byte>.Shared.Return(buffer);
		}

		File.Move(tempPath, path, overwrite: true);
		return hash.Finish();
	}

	private static FileStream OpenRead(string path)
		=> new(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 81920, useAsync: true);

	private static string SanitizeFileName(string name)
	{
		var sanitized
			= string.Concat(name.Where(c => !Path.GetInvalidFileNameChars().Contains(c) && !char.IsControl(c)));
		return string.IsNullOrWhiteSpace(sanitized) ? "file" : sanitized;
	}

	private static string SanitizeExtension(string extension)
	{
		var sanitized = SanitizeFileName(extension.TrimStart('.'));
		return sanitized.Length is 0 or > 16 ? ".bin" : "." + sanitized.ToLowerInvariant();
	}
}
