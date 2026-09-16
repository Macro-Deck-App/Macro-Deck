namespace MacroDeckHost.Infrastructure.Rendering;

internal static class LinuxFontDirectories
{
	private const string NativelyScannedDirectory = "/usr/share/fonts";
	private const string DefaultDataDirectories = "/usr/local/share:/usr/share";

	public static IReadOnlyList<string> Resolve()
	{
		if (!OperatingSystem.IsLinux())
		{
			return [];
		}

		var home = Environment.GetEnvironmentVariable("HOME");
		if (string.IsNullOrWhiteSpace(home))
		{
			home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		}

		return Candidates(Environment.GetEnvironmentVariable, home)
			.Where(Directory.Exists)
			.ToList();
	}

	// The bundled SkiaSharp Linux build has no fontconfig and only scans /usr/share/fonts, so the
	// rest of fontconfig's default search path is added here.
	internal static IReadOnlyList<string> Candidates(Func<string, string?> environment, string? home)
	{
		var directories = new List<string>();

		var dataHome = environment("XDG_DATA_HOME");
		if (IsAbsolute(dataHome))
		{
			directories.Add(dataHome + "/fonts");
		}
		else if (IsAbsolute(home))
		{
			directories.Add(home + "/.local/share/fonts");
		}

		if (IsAbsolute(home))
		{
			directories.Add(home + "/.fonts");
		}

		var dataDirectories = environment("XDG_DATA_DIRS");
		if (string.IsNullOrWhiteSpace(dataDirectories))
		{
			dataDirectories = DefaultDataDirectories;
		}

		directories.AddRange(dataDirectories.Split(':', StringSplitOptions.RemoveEmptyEntries)
			.Where(IsAbsolute)
			.Select(directory => directory + "/fonts"));

		return directories
			.Select(Normalize)
			.Where(directory => !string.Equals(directory, NativelyScannedDirectory, StringComparison.Ordinal))
			.Distinct(StringComparer.Ordinal)
			.ToList();
	}

	private static bool IsAbsolute(string? path) => !string.IsNullOrWhiteSpace(path) && path.StartsWith('/');

	private static string Normalize(string path) =>
		"/" + string.Join('/', path.Split('/', StringSplitOptions.RemoveEmptyEntries));
}
