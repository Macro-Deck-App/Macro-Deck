using System.Collections;
using System.Diagnostics;

namespace MacroDeckHost.Integrations.System.Application;

// Inside an AppImage the inherited PATH finds the bundled xdg-open, which does nothing on Plasma 6,
// and the bundle's library and GTK paths would leak into the opened application.
internal static class AppImageEnvironment
{
	private const UnixFileMode AnyExecute = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;

	private static readonly string[] ForcedByBundle = ["APPDIR", "GTK_THEME", "GDK_BACKEND"];

	private static readonly string[] WorkingDirectoryCandidates = ["OWD", "HOME"];

	private static readonly (string Program, string[] Arguments)[] Launchers =
	[
		("xdg-open", []),
		("gio", ["open"]),
		("gnome-open", []),
		("kde-open", ["--"])
	];

	public static ProcessStartInfo? TryCreateOpener(string target)
		=> TryCreateOpener(target, CurrentEnvironment());

	public static ProcessStartInfo? TryCreateOpener(string target, IReadOnlyDictionary<string, string> environment)
	{
		if (OperatingSystem.IsWindows() ||
			!environment.TryGetValue("APPDIR", out var appDir) ||
			!Path.IsPathRooted(appDir) ||
			appDir.TrimEnd('/').Length == 0)
		{
			return null;
		}

		var outside = Outside(environment, BundleRoots(appDir));
		var (program, arguments) = FindLauncher(outside.GetValueOrDefault("PATH"), appDir);
		if (program is null)
		{
			return null;
		}

		var startInfo = new ProcessStartInfo(program)
		{
			UseShellExecute = false,
			WorkingDirectory = WorkingDirectory(environment)
		};
		foreach (var argument in arguments)
		{
			startInfo.ArgumentList.Add(argument);
		}

		startInfo.ArgumentList.Add(program.EndsWith("/kde-open", StringComparison.Ordinal) || !target.StartsWith('-')
			? target
			: $"./{target}");

		startInfo.Environment.Clear();
		foreach (var (name, value) in outside)
		{
			startInfo.Environment[name] = value;
		}

		return startInfo;
	}

	internal static Dictionary<string, string> Outside(
		IReadOnlyDictionary<string, string> environment,
		IReadOnlyList<string> roots)
	{
		var outside = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach (var (name, value) in environment)
		{
			if (ForcedByBundle.Contains(name, StringComparer.Ordinal))
			{
				continue;
			}

			var entries = value.Split(':');
			if (!entries.Any(entry => IsBundled(entry, roots)))
			{
				outside[name] = value;
				continue;
			}

			var kept = entries.Where(entry => entry.Length > 0 && !IsBundled(entry, roots)).ToArray();
			if (kept.Length > 0)
			{
				outside[name] = string.Join(':', kept);
			}
		}

		return outside;
	}

	internal static IReadOnlyList<string> BundleRoots(string appDir)
	{
		var given = appDir.TrimEnd('/');
		var canonical = Canonicalize(appDir)?.TrimEnd('/');
		return canonical is null || canonical.Length == 0 || canonical == given ? [given] : [given, canonical];
	}

	internal static string? Canonicalize(string path)
	{
		try
		{
			var current = "/";
			foreach (var part in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
			{
				if (part == ".")
				{
					continue;
				}

				if (part == "..")
				{
					current = Path.GetDirectoryName(current) ?? "/";
					continue;
				}

				var next = Path.Combine(current, part);
				current = File.ResolveLinkTarget(next, returnFinalTarget: true)?.FullName ?? next;
			}

			return current;
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			return null;
		}
	}

	internal static bool IsExecutableFile(string path)
	{
		if (OperatingSystem.IsWindows() || !File.Exists(path))
		{
			return false;
		}

		var resolved = File.ResolveLinkTarget(path, returnFinalTarget: true)?.FullName ?? path;
		return (File.GetUnixFileMode(resolved) & AnyExecute) != 0;
	}

	private static bool IsBundled(string entry, IReadOnlyList<string> roots)
		=> roots.Any(root => root.Length > 0 &&
			entry.StartsWith(root, StringComparison.Ordinal) &&
			(entry.Length == root.Length || entry[root.Length] == '/'));

	private static (string? Program, string[] Arguments) FindLauncher(string? searchPath, string appDir)
	{
		var directories = (searchPath ?? string.Empty).Split(':', StringSplitOptions.RemoveEmptyEntries);
		foreach (var (program, arguments) in Launchers)
		{
			var resolved = directories
				.Select(directory => Path.Combine(directory, program))
				.FirstOrDefault(IsExecutableFile);
			if (resolved is not null)
			{
				return (resolved, arguments);
			}
		}

		var bundled = Path.Combine(appDir, "usr", "bin", "xdg-open");
		return IsExecutableFile(bundled) ? (bundled, []) : (null, []);
	}

	private static string WorkingDirectory(IReadOnlyDictionary<string, string> environment)
		=> WorkingDirectoryCandidates
			.Select(name => environment.GetValueOrDefault(name))
			.FirstOrDefault(directory => !string.IsNullOrEmpty(directory) && Directory.Exists(directory)) ?? "/";

	private static Dictionary<string, string> CurrentEnvironment()
	{
		var environment = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
		{
			if (entry.Key is string name && entry.Value is string value)
			{
				environment[name] = value;
			}
		}

		return environment;
	}
}
