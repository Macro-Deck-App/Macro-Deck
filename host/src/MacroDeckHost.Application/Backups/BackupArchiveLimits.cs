using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Backups;

public static class BackupArchiveLimits
{
	public const int MaxOuterEntries = 8;

	public const int MaxManifestBytes = 256 * 1024;

	public const long MaxContentIndexBytes = 64L << 20;

	public const long MaxPayloadBytes = 8L << 30;

	public const int MaxInnerEntries = 200_000;

	public const long MaxSingleEntryBytes = 2L << 30;

	public const long MaxTotalPlaintextBytes = 8L << 30;

	public static bool IsSafeEntryPath(string? path)
	{
		if (string.IsNullOrEmpty(path) || path.Contains('\0') || Path.IsPathRooted(path) || path.Contains('\\'))
		{
			return false;
		}

		var segments = path.Split('/');
		if (segments[0].Length >= 2 && segments[0][1] == ':' && char.IsAsciiLetter(segments[0][0]))
		{
			return false;
		}

		foreach (var segment in segments)
		{
			// A colon is an ordinary file name character on macOS and Linux, and the writer archives such
			// names; on Windows it would address a drive or an alternate data stream.
			if (segment is "" or "." or ".." || OperatingSystem.IsWindows() && segment.Contains(':'))
			{
				return false;
			}
		}

		return true;
	}

	public static bool IsRestorableEntry(string? path, BackupComponentGroup component)
	{
		return IsSafeEntryPath(path)
			&& !BackupComponentGroups.IsExcluded(path!)
			&& !BackupComponentGroups.IsSidecar(path!)
			&& BackupComponentGroups.Owner(path!) == component;
	}

	public static string? ResolveInside(string root, string? relativePath)
	{
		if (string.IsNullOrEmpty(relativePath) || relativePath.Contains('\0'))
		{
			return null;
		}

		var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
		var full = Path.GetFullPath(Path.Combine(fullRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));

		return full.StartsWith(fullRoot, StringComparison.Ordinal) ? full : null;
	}
}
