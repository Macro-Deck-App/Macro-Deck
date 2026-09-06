using System.Buffers;

namespace MacroDeck.Plugin.Packaging.Artifacts;

/// <summary>Why an archive entry was refused.</summary>
public enum PluginArtifactEntryRejection
{
	/// <summary>Empty, whitespace, or containing a NUL.</summary>
	MalformedName,

	/// <summary>Rooted, or qualified with a Windows drive letter.</summary>
	AbsolutePath,

	/// <summary>Contains a <c>..</c> segment, or resolves outside the target directory.</summary>
	PathTraversal,

	/// <summary>Longer than <see cref="PluginArtifactLimits.MaxPathLength"/> or deeper than
	/// <see cref="PluginArtifactLimits.MaxPathDepth"/>.</summary>
	PathTooLong,

	/// <summary>A segment carries a character no file system here accepts, or is a reserved Windows
	/// device name.</summary>
	IllegalSegment,

	/// <summary>Not a regular file or directory - a symlink, fifo, socket or device node.</summary>
	UnsafeFileType
}

/// <summary>The outcome of judging one archive entry.</summary>
public readonly record struct PluginArtifactEntryDecision
{
	private PluginArtifactEntryDecision(bool allowed,
		string? resolvedPath,
		PluginArtifactEntryRejection? rejection,
		string? message)
	{
		Allowed = allowed;
		ResolvedPath = resolvedPath;
		Rejection = rejection;
		Message = message;
	}

	public bool Allowed { get; }

	/// <summary>The absolute destination, set only when <see cref="Allowed"/>.</summary>
	public string? ResolvedPath { get; }

	public PluginArtifactEntryRejection? Rejection { get; }

	public string? Message { get; }

	public static PluginArtifactEntryDecision Allow(string resolvedPath)
	{
		return new PluginArtifactEntryDecision(true, resolvedPath, null, null);
	}

	public static PluginArtifactEntryDecision Reject(PluginArtifactEntryRejection rejection, string message)
	{
		return new PluginArtifactEntryDecision(false, null, rejection, message);
	}
}

/// <summary>
/// The pure, security-critical decision about where - and whether - one archive entry may be written.
/// Split out from the extractor so it is testable without building an archive, following the same split
/// the ADB platform-tools installer already uses for its zip-slip guard.
/// </summary>
public static class PluginArtifactEntryPolicy
{
	/// <summary>The Unix <c>S_IFMT</c> mask over the high 16 bits of a ZIP entry's external attributes.
	/// </summary>
	private const int UnixFileTypeMask = 0xF000;

	private const int UnixRegularFile = 0x8000;

	private const int UnixDirectory = 0x4000;

	/// <summary>
	/// A fixed set rather than <see cref="Path.GetInvalidFileNameChars"/>, which on Unix contains only
	/// NUL and the separator. Using the platform's own list would mean an artifact carrying <c>*</c> or
	/// <c>|</c> in a name installs on Linux and fails on Windows, which is exactly the cross-platform
	/// divergence this policy exists to remove.
	/// </summary>
	private static readonly SearchValues<char> _illegalSegmentCharacters =
		SearchValues.Create(['\0', '<', '>', ':', '"', '|', '?', '*']);

	/// <summary>Unwritable on Windows with or without an extension, so an artifact carrying one is a
	/// cross-platform trap whether or not it was meant maliciously.</summary>
	private static readonly HashSet<string> _reservedWindowsNames = new(StringComparer.OrdinalIgnoreCase)
	{
		"CON", "PRN", "AUX", "NUL",
		"COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
		"LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
	};

	/// <summary>
	/// Judges <paramref name="entryFullName"/> against <paramref name="targetDirectory"/> and returns the
	/// absolute destination when it is safe to write.
	/// </summary>
	public static PluginArtifactEntryDecision Resolve(string? entryFullName, string targetDirectory)
	{
		if (string.IsNullOrWhiteSpace(entryFullName) || entryFullName.Contains('\0', StringComparison.Ordinal))
		{
			return PluginArtifactEntryDecision.Reject(PluginArtifactEntryRejection.MalformedName,
				"The archive entry has an empty or malformed name.");
		}

		if (entryFullName.Length > PluginArtifactLimits.MaxPathLength)
		{
			return PluginArtifactEntryDecision.Reject(PluginArtifactEntryRejection.PathTooLong,
				$"The archive entry '{entryFullName}' exceeds the " +
				$"{PluginArtifactLimits.MaxPathLength}-character path limit.");
		}

		// Normalised so an entry written with the other OS's separator is judged identically here - a
		// hostile archive is not obliged to use this host's separator convention.
		var normalized = entryFullName
			.Replace('\\', Path.DirectorySeparatorChar)
			.Replace('/', Path.DirectorySeparatorChar);

		// ':' is an ordinary filename character on Unix, so Path.IsPathRooted would not catch "C:\evil"
		// there. Checked explicitly so the guard does not depend on which OS it runs on.
		var hasWindowsDriveLetter = normalized.Length >= 2 &&
			normalized[1] == ':' &&
			normalized[0] is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z');

		if (hasWindowsDriveLetter || Path.IsPathRooted(normalized))
		{
			return PluginArtifactEntryDecision.Reject(PluginArtifactEntryRejection.AbsolutePath,
				$"The archive entry '{entryFullName}' has an absolute path.");
		}

		var segments = normalized.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
		if (segments.Length == 0)
		{
			return PluginArtifactEntryDecision.Reject(PluginArtifactEntryRejection.MalformedName,
				$"The archive entry '{entryFullName}' names no path segment.");
		}

		if (segments.Length > PluginArtifactLimits.MaxPathDepth)
		{
			return PluginArtifactEntryDecision.Reject(PluginArtifactEntryRejection.PathTooLong,
				$"The archive entry '{entryFullName}' is deeper than " +
				$"{PluginArtifactLimits.MaxPathDepth} segments.");
		}

		foreach (var segment in segments)
		{
			if (segment == "..")
			{
				return PluginArtifactEntryDecision.Reject(PluginArtifactEntryRejection.PathTraversal,
					$"The archive entry '{entryFullName}' contains a parent-directory segment.");
			}

			if (segment == ".")
			{
				continue;
			}

			if (segment.AsSpan().IndexOfAny(_illegalSegmentCharacters) >= 0)
			{
				return PluginArtifactEntryDecision.Reject(PluginArtifactEntryRejection.IllegalSegment,
					$"The archive entry '{entryFullName}' contains an illegal path segment.");
			}

			var withoutExtension = Path.GetFileNameWithoutExtension(segment);
			if (_reservedWindowsNames.Contains(withoutExtension))
			{
				return PluginArtifactEntryDecision.Reject(PluginArtifactEntryRejection.IllegalSegment,
					$"The archive entry '{entryFullName}' uses the reserved name '{withoutExtension}'.");
			}
		}

		var targetRoot = Path.GetFullPath(targetDirectory);
		var rootWithSeparator = targetRoot.EndsWith(Path.DirectorySeparatorChar)
			? targetRoot
			: targetRoot + Path.DirectorySeparatorChar;

		var combined = Path.GetFullPath(Path.Combine(targetRoot, normalized));
		if (!combined.StartsWith(rootWithSeparator, StringComparison.Ordinal))
		{
			return PluginArtifactEntryDecision.Reject(PluginArtifactEntryRejection.PathTraversal,
				$"The archive entry '{entryFullName}' would extract outside the target directory.");
		}

		return PluginArtifactEntryDecision.Allow(combined);
	}

	/// <summary>
	/// True when a ZIP entry's external attributes describe something other than a regular file or a
	/// directory - a symlink, fifo, socket or device node. Attributes written by a Windows packer carry no
	/// Unix mode at all, which reads as zero and is treated as a regular file.
	/// </summary>
	public static bool IsUnsafeFileType(int externalAttributes)
	{
		var unixMode = (externalAttributes >> 16) & 0xFFFF;
		var fileType = unixMode & UnixFileTypeMask;

		return fileType is not 0 and not UnixRegularFile and not UnixDirectory;
	}
}
