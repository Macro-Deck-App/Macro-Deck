using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages;
using MacroDeckHost.Application.Ui.Transport.Messages.Filesystem;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Application.Ui.Handlers;

public class GetFilesystemEntriesRequestMessageHandler
	: IUiTransportMessageHandler<GetFilesystemEntriesRequest, GetFilesystemEntriesResponse>
{
	public ValueTask<GetFilesystemEntriesResponse> Handle(
		GetFilesystemEntriesRequest request,
		CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(request.Path))
		{
			return ValueTask.FromResult(ListRoots());
		}

		var fullPath = Path.GetFullPath(request.Path);
		if (!Directory.Exists(fullPath))
		{
			return ValueTask.FromResult(new GetFilesystemEntriesResponse
			{
				Path = fullPath,
				Error = new TransportError
				{
					Code = "DIRECTORY_NOT_FOUND",
					Message = AppStrings.Errors.Filesystem.DirectoryNotFound(path: fullPath)
				}
			});
		}

		try
		{
			var response = new GetFilesystemEntriesResponse
			{
				Path = fullPath,
				ParentPath = Path.GetDirectoryName(fullPath)
			};

			foreach (var directory in Directory.EnumerateDirectories(fullPath).Order())
			{
				if (IsHidden(directory))
				{
					continue;
				}

				response.Entries.Add(new FilesystemEntry
				{
					Name = Path.GetFileName(directory),
					Path = directory,
					IsDirectory = true
				});
			}

			if (!request.DirectoriesOnly)
			{
				var extensions = request.Extensions?
					.Select(e => e.StartsWith('.') ? e : $".{e}")
					.ToHashSet(StringComparer.OrdinalIgnoreCase);

				foreach (var file in Directory.EnumerateFiles(fullPath).Order())
				{
					if (IsHidden(file))
					{
						continue;
					}

					if (extensions is { Count: > 0 } && !extensions.Contains(Path.GetExtension(file)))
					{
						continue;
					}

					response.Entries.Add(new FilesystemEntry
					{
						Name = Path.GetFileName(file),
						Path = file,
						IsDirectory = false
					});
				}
			}

			return ValueTask.FromResult(response);
		}
		catch (UnauthorizedAccessException)
		{
			return ValueTask.FromResult(new GetFilesystemEntriesResponse
			{
				Path = fullPath,
				Error = new TransportError
				{
					Code = "ACCESS_DENIED",
					Message = AppStrings.Errors.Filesystem.AccessDenied(path: fullPath)
				}
			});
		}
	}

	private static GetFilesystemEntriesResponse ListRoots()
	{
		var response = new GetFilesystemEntriesResponse { Path = string.Empty };

		if (OperatingSystem.IsWindows())
		{
			foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
			{
				response.Entries.Add(new FilesystemEntry
				{
					Name = drive.Name,
					Path = drive.RootDirectory.FullName,
					IsDirectory = true
				});
			}
		}
		else
		{
			response.Entries.Add(new FilesystemEntry
			{
				Name = "/",
				Path = "/",
				IsDirectory = true
			});
		}

		var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		if (!string.IsNullOrEmpty(home))
		{
			response.Entries.Add(new FilesystemEntry
			{
				Name = Path.GetFileName(home.TrimEnd(Path.DirectorySeparatorChar)),
				Path = home,
				IsDirectory = true
			});
		}

		return response;
	}

	private static bool IsHidden(string path)
	{
		if (Path.GetFileName(path).StartsWith('.'))
		{
			return true;
		}

		try
		{
			return (File.GetAttributes(path) & FileAttributes.Hidden) != 0;
		}
		catch (IOException)
		{
			return false;
		}
		catch (UnauthorizedAccessException)
		{
			return true;
		}
	}
}
