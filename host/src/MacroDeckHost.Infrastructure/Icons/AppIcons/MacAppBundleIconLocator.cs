namespace MacroDeckHost.Infrastructure.Icons.AppIcons;

internal static class MacAppBundleIconLocator
{
	private const string AppBundleExtension = ".app";
	private const string ContentsDirectoryName = "Contents";
	private const string ResourcesDirectoryName = "Resources";
	private const string InfoPlistFileName = "Info.plist";
	private const string IcnsExtension = ".icns";

	private static readonly char[] _separators = ['/', '\\'];

	public static bool IsAppBundle(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return false;
		}

		return TrimTrailingSeparators(path)
			.EndsWith(AppBundleExtension, StringComparison.OrdinalIgnoreCase);
	}

	public static string? TryResolveIconFile(string bundlePath)
	{
		if (string.IsNullOrWhiteSpace(bundlePath))
		{
			return null;
		}

		var contents = Path.Combine(TrimTrailingSeparators(bundlePath),
			ContentsDirectoryName);
		var resources = Path.Combine(contents, ResourcesDirectoryName);

		var declared = TryReadDeclaredIconFileName(contents);
		if (declared is not null && TryFindByName(resources, declared) is { } declaredFile)
		{
			return declaredFile;
		}

		return TryFindLargestIcns(resources);
	}

	private static string? TryReadDeclaredIconFileName(string contentsDirectory)
	{
		var plistPath = Path.Combine(contentsDirectory, InfoPlistFileName);

		string xml;
		try
		{
			if (!File.Exists(plistPath))
			{
				return null;
			}

			xml = File.ReadAllText(plistPath);
		}
		catch (Exception exception) when (exception is IOException
			or UnauthorizedAccessException
			or ArgumentException
			or NotSupportedException)
		{
			return null;
		}

		var declared = PropertyListIconParser.TryReadIconFileName(xml);
		if (declared is null)
		{
			return null;
		}

		return Path.GetExtension(declared).Length == 0
			? declared + IcnsExtension
			: declared;
	}

	private static string? TryFindByName(string resourcesDirectory, string fileName)
	{
		foreach (var candidate in EnumerateFiles(resourcesDirectory))
		{
			if (string.Equals(Path.GetFileName(candidate), fileName, StringComparison.OrdinalIgnoreCase))
			{
				return candidate;
			}
		}

		return null;
	}

	private static string? TryFindLargestIcns(string resourcesDirectory)
	{
		string? largest = null;
		var largestLength = -1L;

		foreach (var candidate in EnumerateFiles(resourcesDirectory)
			.Order(StringComparer.Ordinal))
		{
			if (!Path.GetExtension(candidate)
				.Equals(IcnsExtension, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			var length = TryGetLength(candidate);
			if (length > largestLength)
			{
				largest = candidate;
				largestLength = length;
			}
		}

		return largest;
	}

	private static long TryGetLength(string path)
	{
		try
		{
			return new FileInfo(path).Length;
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			return -1;
		}
	}

	private static string[] EnumerateFiles(string directory)
	{
		try
		{
			return Directory.Exists(directory) ? Directory.GetFiles(directory) : [];
		}
		catch (Exception exception) when (exception is IOException
			or UnauthorizedAccessException
			or ArgumentException)
		{
			return [];
		}
	}

	private static string TrimTrailingSeparators(string path)
		=> path.TrimEnd(_separators);
}
