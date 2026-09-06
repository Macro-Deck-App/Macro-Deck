using MacroDeckHost.Application.Backups;
using MacroDeckHost.Application.Paths;
using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport.Messages;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Api.Support;

public static class BackupHttp
{
	public const long MaxUploadBytes = 4L << 30;

	public static async Task<Result<string, BackupError>> StageUpload(IFormFile? file,
		IMacroDeckPaths paths,
		CancellationToken cancellationToken)
	{
		if (file is null || file.Length == 0)
		{
			return Result.Fail<string, BackupError>(BackupError.ValidationError, "A file is required");
		}

		if (file.Length > MaxUploadBytes)
		{
			return Result.Fail<string, BackupError>(BackupError.TooLarge, "The file is too large to upload");
		}

		Directory.CreateDirectory(paths.RestoreStagingDirectory);
		var fileName = Guid.NewGuid().ToString("N") + BackupFileNames.Extension;
		var stagedPath = Path.Combine(paths.RestoreStagingDirectory, fileName);
		var tooLarge = false;

		try
		{
			await using (var destination
				= new FileStream(stagedPath, FileMode.Create, FileAccess.Write, FileShare.None))
			await using (var source = file.OpenReadStream())
			{
				var buffer = new byte[81920];
				long total = 0;
				int read;
				while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
				{
					total += read;
					if (total > MaxUploadBytes)
					{
						tooLarge = true;
						break;
					}

					await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
				}
			}
		}
		catch (Exception e) when (e is IOException or UnauthorizedAccessException)
		{
			DeleteStaged(stagedPath);
			return Result.Fail<string, BackupError>(BackupError.StorageFailure, "The upload could not be staged");
		}

		// The FileStream is closed by the time this runs, so the delete below cannot race an open handle
		// on platforms - Windows in particular - that lock a file for as long as it is held open.
		if (tooLarge)
		{
			DeleteStaged(stagedPath);
			return Result.Fail<string, BackupError>(BackupError.TooLarge, "The file is too large to upload");
		}

		return Result.Ok<string, BackupError>(stagedPath);
	}

	public static void DeleteStaged(string? path)
	{
		if (string.IsNullOrEmpty(path))
		{
			return;
		}

		try
		{
			if (File.Exists(path))
			{
				File.Delete(path);
			}
		}
		catch (Exception e) when (e is IOException or UnauthorizedAccessException)
		{
		}
	}

	public static Result<BackupError> ValidatePath(string? path)
	{
		var validation = ArchivePathReader.Validate(path, [BackupFileNames.Extension], MaxUploadBytes);
		return validation.Success
			? Result.Ok<BackupError>()
			: Result.Fail(MapPathError(validation.Error!.Value), validation.ErrorMessage);
	}

	public static TransportError ToTransportError(BackupError error, string? message)
		=> BackupDtoMapper.ToTransportError(error, message);

	private static BackupError MapPathError(PortabilityError error)
		=> error switch
		{
			PortabilityError.NotFound => BackupError.NotFound,
			_ => BackupError.ValidationError
		};
}
