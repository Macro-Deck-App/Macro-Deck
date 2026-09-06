using System.Runtime.Versioning;
using MacroDeckHost.Application.Applications;
using MacroDeckHost.Application.Icons;
using MacroDeckHost.Integrations.System.DesktopEntries;

namespace MacroDeckHost.Infrastructure.Applications;

public sealed class ApplicationPathResolver : IApplicationPathResolver
{
	private const int MaxDesktopEntryLines = 2048;

	private static readonly HashSet<string> _executableExtensions = new(StringComparer.OrdinalIgnoreCase)
	{
		".exe", ".com", ".bat", ".cmd", ".lnk", ".url", ".sh"
	};

	public ResolvedApplication? Resolve(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return null;
		}

		var target = FollowLink(IconImportFiles.TrimTrailingSeparators(path.Trim()));
		var extension = Path.GetExtension(target);

		if (DesktopEntryParser.IsDesktopEntry(target))
		{
			return ResolveDesktopEntry(target);
		}

		if (extension.Equals(".app", StringComparison.OrdinalIgnoreCase))
		{
			return Directory.Exists(target) ? Application(target) : null;
		}

		if (!File.Exists(target))
		{
			return null;
		}

		if (_executableExtensions.Contains(extension))
		{
			return Application(target);
		}

		return !OperatingSystem.IsWindows() && IsExecutable(target)
			? Application(target)
			: null;
	}

	private static ResolvedApplication? ResolveDesktopEntry(string path)
	{
		if (!File.Exists(path))
		{
			return null;
		}

		var lines = ReadCappedLines(path);
		var command = DesktopEntryParser.TryReadCommand(lines);
		if (command is null)
		{
			return null;
		}

		var name = DesktopEntryParser.TryReadName(lines) ?? Path.GetFileNameWithoutExtension(command.Program);
		return new ResolvedApplication(command.Program, name, command.Arguments);
	}

	private static List<string> ReadCappedLines(string path)
	{
		try
		{
			return File.ReadLines(path).Take(MaxDesktopEntryLines).ToList();
		}
		catch (IOException)
		{
			return [];
		}
		catch (UnauthorizedAccessException)
		{
			return [];
		}
	}

	private static ResolvedApplication Application(string path)
		=> new(path, Path.GetFileNameWithoutExtension(path));

	private static string FollowLink(string path)
	{
		try
		{
			FileSystemInfo info = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
			return info.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? path;
		}
		catch (Exception exception) when (exception is IOException
			or UnauthorizedAccessException
			or ArgumentException)
		{
			return path;
		}
	}

	[UnsupportedOSPlatform("windows")]
	private static bool IsExecutable(string path)
	{
		try
		{
			const UnixFileMode executable
				= UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;

			return (File.GetUnixFileMode(path) & executable) != 0;
		}
		catch (Exception exception) when (exception is IOException
			or UnauthorizedAccessException
			or PlatformNotSupportedException)
		{
			return false;
		}
	}
}
