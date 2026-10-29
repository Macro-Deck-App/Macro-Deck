using MacroDeckHost.Application.Backups;
using MacroDeckHost.Application.Paths;
using Serilog;

namespace MacroDeckHost.Infrastructure.Backups.Restore;

public static class BackupLeftoverSweeper
{
	public static void Sweep(IMacroDeckPaths paths)
	{
		if (File.Exists(Path.Combine(paths.RestoreStagingDirectory, PendingRestoreDocument.FileName)))
		{
			return;
		}

		FileStream lockFile;
		try
		{
			lockFile = new FileStream(Path.Combine(paths.DataRootDirectory, StagedRestoreApplier.LockFileName),
				FileMode.OpenOrCreate,
				FileAccess.ReadWrite,
				FileShare.None);
		}
		catch (Exception e) when (e is IOException or UnauthorizedAccessException)
		{
			Log.Warning(e, "The data directory is locked; leaving backup leftovers for the next start");
			return;
		}

		using (lockFile)
		{
			DeleteContents(paths.RestoreStagingDirectory);
			DeleteContents(Path.Combine(paths.BackupsDirectory, BackupService.StagingDirectoryName));

			if (!Directory.Exists(paths.BackupsDirectory))
			{
				return;
			}

			var partialPattern = "*" + BackupFileNames.IncompleteExtension;
			foreach (var partial in Directory.EnumerateFiles(paths.BackupsDirectory, partialPattern))
			{
				Delete(partial, false);
			}
		}
	}

	private static void DeleteContents(string directory)
	{
		if (!Directory.Exists(directory))
		{
			return;
		}

		foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
		{
			Delete(entry, Directory.Exists(entry));
		}
	}

	private static void Delete(string path, bool isDirectory)
	{
		try
		{
			if (isDirectory)
			{
				Directory.Delete(path, true);
			}
			else
			{
				File.Delete(path);
			}
		}
		catch (Exception e) when (e is IOException or UnauthorizedAccessException)
		{
			Log.Warning(e, "Could not delete the backup leftover {Path}", path);
		}
	}
}
