using System.IO.Compression;
using MacroDeckHost.Application.Adb;
using MacroDeckHost.Application.Paths;
using MacroDeckHost.Domain.Common;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.Adb;

internal sealed class AdbPlatformToolsInstaller : IAdbPlatformToolsInstaller
{
	private const string WindowsDownloadUrl =
		"https://dl.google.com/android/repository/platform-tools-latest-windows.zip";

	private const string MacOsDownloadUrl =
		"https://dl.google.com/android/repository/platform-tools-latest-darwin.zip";

	private const string LinuxDownloadUrl =
		"https://dl.google.com/android/repository/platform-tools-latest-linux.zip";

	// Generous for a ~5-15 MB archive even on a slow connection, but bounded so a stalled download
	// cannot hang the request forever.
	private static readonly TimeSpan _downloadTimeout = TimeSpan.FromMinutes(5);

	private const UnixFileMode ExecutableFileMode = UnixFileMode.UserRead |
		UnixFileMode.UserWrite |
		UnixFileMode.UserExecute |
		UnixFileMode.GroupRead |
		UnixFileMode.GroupExecute |
		UnixFileMode.OtherRead |
		UnixFileMode.OtherExecute;

	private readonly IHttpClientFactory _httpClientFactory;
	private readonly IMacroDeckPaths _paths;
	private readonly ILogger _logger;

	internal AdbPlatformToolsInstaller(IHttpClientFactory httpClientFactory, IMacroDeckPaths paths, ILogger logger)
	{
		_httpClientFactory = httpClientFactory;
		_paths = paths;
		_logger = logger.ForContext<AdbPlatformToolsInstaller>();
	}

	public static string? ResolveDownloadUrl(bool isWindows, bool isMacOs, bool isLinux)
	{
		if (isWindows)
		{
			return WindowsDownloadUrl;
		}

		if (isMacOs)
		{
			return MacOsDownloadUrl;
		}

		return isLinux ? LinuxDownloadUrl : null;
	}

	public static Result<string, AdbFailureCode> ResolveEntryDestination(string entryFullName, string targetDirectory)
	{
		var targetRoot = Path.GetFullPath(targetDirectory);
		var normalizedRoot = targetRoot.EndsWith(Path.DirectorySeparatorChar)
			? targetRoot
			: targetRoot + Path.DirectorySeparatorChar;

		// Normalised so an entry written with the other OS's separator ("platform-tools\adb" from a
		// Windows-built archive, or a hostile "\evil") is judged the same way regardless of which OS this
		// runs on - a malicious archive is not required to use this OS's own separator convention.
		var normalizedEntry = entryFullName
			.Replace('\\', Path.DirectorySeparatorChar)
			.Replace('/', Path.DirectorySeparatorChar);

		var hasWindowsDriveLetter = normalizedEntry.Length >= 2 &&
			normalizedEntry[1] == ':' &&
			normalizedEntry[0] is >= 'A' and <= 'Z' or >= 'a' and <= 'z';

		if (hasWindowsDriveLetter || Path.IsPathRooted(normalizedEntry))
		{
			return Result.Fail<string, AdbFailureCode>(AdbFailureCode.CommandFailed,
				$"The archive entry '{entryFullName}' has an unsafe path and was rejected.");
		}

		var combined = Path.GetFullPath(Path.Combine(targetRoot, normalizedEntry));

		if (!combined.StartsWith(normalizedRoot, StringComparison.Ordinal))
		{
			return Result.Fail<string, AdbFailureCode>(AdbFailureCode.CommandFailed,
				$"The archive entry '{entryFullName}' would extract outside the target directory.");
		}

		return Result.Ok<string, AdbFailureCode>(combined);
	}

	public async Task<Result<string, AdbFailureCode>> InstallAsync(CancellationToken cancellationToken)
	{
		var url = ResolveDownloadUrl(OperatingSystem.IsWindows(), OperatingSystem.IsMacOS(), OperatingSystem.IsLinux());
		if (url is null)
		{
			return Result.Fail<string, AdbFailureCode>(AdbFailureCode.Unsupported,
				"Android platform-tools has no download available for this operating system.");
		}

		var targetDirectory = Path.Combine(_paths.DataRootDirectory, "platform-tools");
		var tempFile = Path.Combine(Path.GetTempPath(), $"macro-deck-platform-tools-{Guid.NewGuid():N}.zip");

		try
		{
			using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
			{
				linkedCts.CancelAfter(_downloadTimeout);
				await DownloadToFileAsync(url, tempFile, linkedCts.Token);
			}

			if (Directory.Exists(targetDirectory))
			{
				Directory.Delete(targetDirectory, recursive: true);
			}

			Directory.CreateDirectory(targetDirectory);

			return ExtractArchive(tempFile, targetDirectory);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception ex) when (ex is HttpRequestException
			or IOException
			or InvalidDataException
			or OperationCanceledException
			or UnauthorizedAccessException)
		{
			_logger.Warning(ex, "Failed to download or install Android platform-tools");
			return Result.Fail<string, AdbFailureCode>(AdbFailureCode.CommandFailed,
				"Android platform-tools could not be downloaded. Check your internet connection and try again.");
		}
		finally
		{
			TryDeleteFile(tempFile);
		}
	}

	private async Task DownloadToFileAsync(string url, string destinationPath, CancellationToken cancellationToken)
	{
		var client = _httpClientFactory.CreateClient();

		using var response
			= await client.GetAsync(new Uri(url), HttpCompletionOption.ResponseHeadersRead, cancellationToken);
		response.EnsureSuccessStatusCode();

		await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
		await using var destination
			= new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
		await source.CopyToAsync(destination, cancellationToken);
	}

	private static Result<string, AdbFailureCode> ExtractArchive(string archivePath, string targetDirectory)
	{
		var executableName = OperatingSystem.IsWindows() ? "adb.exe" : "adb";
		string? adbPath = null;
		var extractedFiles = new List<string>();

		using (var archive = ZipFile.OpenRead(archivePath))
		{
			foreach (var entry in archive.Entries)
			{
				var destinationResult = ResolveEntryDestination(entry.FullName, targetDirectory);
				if (!destinationResult.Success)
				{
					return Result.Fail<string, AdbFailureCode>(destinationResult.Error!.Value,
						destinationResult.ErrorMessage);
				}

				var destination = destinationResult.Data!;

				if (entry.Name.Length == 0)
				{
					Directory.CreateDirectory(destination);
					continue;
				}

				var destinationDirectory = Path.GetDirectoryName(destination);
				if (!string.IsNullOrEmpty(destinationDirectory))
				{
					Directory.CreateDirectory(destinationDirectory);
				}

				entry.ExtractToFile(destination, overwrite: true);
				extractedFiles.Add(destination);

				if (string.Equals(Path.GetFileName(destination), executableName, StringComparison.Ordinal))
				{
					adbPath = destination;
				}
			}
		}

		if (adbPath is null)
		{
			return Result.Fail<string, AdbFailureCode>(AdbFailureCode.CommandFailed,
				"The downloaded archive did not contain an adb executable.");
		}

		if (!OperatingSystem.IsWindows())
		{
			foreach (var file in extractedFiles)
			{
				if (file == adbPath || Path.GetExtension(file).Length == 0)
				{
					File.SetUnixFileMode(file, ExecutableFileMode);
				}
			}
		}

		return Result.Ok<string, AdbFailureCode>(adbPath);
	}

	private static void TryDeleteFile(string path)
	{
		try
		{
			if (File.Exists(path))
			{
				File.Delete(path);
			}
		}
		catch (IOException)
		{
		}
		catch (UnauthorizedAccessException)
		{
		}
	}
}
