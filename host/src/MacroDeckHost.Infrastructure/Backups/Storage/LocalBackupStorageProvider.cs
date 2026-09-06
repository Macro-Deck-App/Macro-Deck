using System.Runtime.CompilerServices;
using MacroDeckHost.Application.Backups;
using MacroDeckHost.Application.Backups.Storage;
using MacroDeckHost.Application.Paths;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Infrastructure.Backups.Storage;

public sealed class LocalBackupStorageProvider : IBackupStorageProvider
{
	public const string Id = "local";

	private readonly IMacroDeckPaths _paths;

	public LocalBackupStorageProvider(IMacroDeckPaths paths) => _paths = paths;

	public string ProviderId => Id;

	public string DisplayName => "This computer";

	public BackupStorageCapabilities Capabilities => new(IsRemote: false, SupportsDelete: true, MaxObjectBytes: null);

	public ValueTask<BackupStorageAvailability> GetAvailability(CancellationToken cancellationToken = default)
	{
		try
		{
			Directory.CreateDirectory(_paths.BackupsDirectory);

			return ValueTask.FromResult(new BackupStorageAvailability(true, null));
		}
		catch (Exception e) when (e is IOException or UnauthorizedAccessException)
		{
			return ValueTask.FromResult(new BackupStorageAvailability(false, e.Message));
		}
	}

	public async IAsyncEnumerable<BackupStorageObject> List(
		[EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		Directory.CreateDirectory(_paths.BackupsDirectory);

		// Only the final extension is listed. An interrupted write leaves a .part file behind, which is
		// therefore never mistaken for a usable backup.
		foreach (var path in Directory.EnumerateFiles(_paths.BackupsDirectory, "*" + BackupFileNames.Extension))
		{
			cancellationToken.ThrowIfCancellationRequested();

			var info = new FileInfo(path);
			yield return new BackupStorageObject(Id,
				info.Name,
				info.Name,
				info.Length,
				new DateTimeOffset(info.CreationTimeUtc, TimeSpan.Zero));

			await Task.Yield();
		}
	}

	public Task<Result<Stream, BackupError>> OpenRead(string storageId, CancellationToken cancellationToken = default)
	{
		if (!TryResolve(storageId, out var path) || !File.Exists(path))
		{
			return Task.FromResult(Result.Fail<Stream, BackupError>(BackupError.NotFound,
				"The backup is not available."));
		}

		Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);

		return Task.FromResult(Result.Ok<Stream, BackupError>(stream));
	}

	public async Task<Result<BackupStorageObject, BackupError>> Write(BackupStorageWriteRequest request,
		CancellationToken cancellationToken = default)
	{
		Directory.CreateDirectory(_paths.BackupsDirectory);

		var finalPath = Path.Combine(_paths.BackupsDirectory, request.SuggestedName + BackupFileNames.Extension);
		var incompletePath = finalPath + ".part";

		try
		{
			if (CanAdoptStagedFile(request, finalPath))
			{
				File.Move(request.StagedFilePath!, incompletePath, overwrite: true);
			}
			else
			{
				await using var destination = new FileStream(incompletePath,
					FileMode.Create,
					FileAccess.Write,
					FileShare.None);
				await request.WriteContent(destination, cancellationToken);
				await destination.FlushAsync(cancellationToken);
				destination.Flush(flushToDisk: true);
			}

			File.Move(incompletePath, finalPath, overwrite: true);
			var info = new FileInfo(finalPath);
			request.Progress?.Report(new BackupTransferProgress(info.Length, info.Length));

			return Result.Ok<BackupStorageObject, BackupError>(new BackupStorageObject(Id,
				info.Name,
				info.Name,
				info.Length,
				new DateTimeOffset(info.CreationTimeUtc, TimeSpan.Zero)));
		}
		catch (Exception e) when (e is IOException or UnauthorizedAccessException)
		{
			TryDelete(incompletePath);

			return Result.Fail<BackupStorageObject, BackupError>(BackupError.StorageFailure, e.Message);
		}
	}

	public Task<Result<BackupError>> Delete(string storageId, CancellationToken cancellationToken = default)
	{
		if (!TryResolve(storageId, out var path))
		{
			return Task.FromResult(Result.Fail(BackupError.NotFound, "The backup is not available."));
		}

		try
		{
			File.Delete(path);

			return Task.FromResult(Result.Ok<BackupError>());
		}
		catch (Exception e) when (e is IOException or UnauthorizedAccessException)
		{
			return Task.FromResult(Result.Fail(BackupError.StorageFailure, e.Message));
		}
	}

	private static bool CanAdoptStagedFile(BackupStorageWriteRequest request, string finalPath)
		=> request.StagedFilePath is not null &&
			File.Exists(request.StagedFilePath) &&
			string.Equals(Path.GetPathRoot(Path.GetFullPath(request.StagedFilePath)),
				Path.GetPathRoot(Path.GetFullPath(finalPath)),
				StringComparison.OrdinalIgnoreCase);

	private bool TryResolve(string storageId, out string path)
	{
		path = string.Empty;

		if (string.IsNullOrWhiteSpace(storageId) ||
			storageId.Contains('/') ||
			storageId.Contains('\\') ||
			!storageId.EndsWith(BackupFileNames.Extension, StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		path = Path.Combine(_paths.BackupsDirectory, storageId);

		return true;
	}

	private static void TryDelete(string path)
	{
		try
		{
			File.Delete(path);
		}
		catch (Exception e) when (e is IOException or UnauthorizedAccessException)
		{
		}
	}
}
