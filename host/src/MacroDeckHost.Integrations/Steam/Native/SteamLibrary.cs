using System.Globalization;
using MacroDeckHost.Integrations.Steam.State;

namespace MacroDeckHost.Integrations.Steam.Native;

internal sealed class SteamLibrary
{
	private const int MaximumFileBytes = 4 * 1024 * 1024;

	private const int InstalledStateFlag = 4;

	private static readonly TimeSpan _maximumAge = TimeSpan.FromSeconds(60);

	private static readonly HashSet<int> _toolAppIds = [228980, 1070560, 1391110, 1628350, 1493710, 2180100];

	private static readonly string[] _toolNamePrefixes =
	[
		"Proton ",
		"Proton-",
		"Steam Linux Runtime",
		"Steamworks Common Redistributables"
	];

	private readonly TimeProvider _time;

	private string? _signature;
	private DateTimeOffset _scannedAt;
	private IReadOnlyList<SteamGame> _games = [];

	public SteamLibrary(TimeProvider time)
	{
		_time = time;
	}

	public IReadOnlyList<SteamGame> Scan(string steamRoot, IReadOnlyList<string> extraFolders)
	{
		var folders = LibraryFolders(steamRoot, extraFolders);
		var signature = Signature(steamRoot, folders);
		var now = _time.GetUtcNow();

		if (signature == _signature && now - _scannedAt < _maximumAge)
		{
			return _games;
		}

		var scanned = ReadGames(folders);
		_signature = signature;
		_scannedAt = now;

		if (!scanned.SequenceEqual(_games))
		{
			_games = scanned;
		}

		return _games;
	}

	internal static IReadOnlyList<string> LibraryFolders(string steamRoot, IReadOnlyList<string> extraFolders)
	{
		var folders = new List<string>();

		void Add(string? candidate)
		{
			if (string.IsNullOrWhiteSpace(candidate))
			{
				return;
			}

			var folder = candidate.Trim();
			string full;
			try
			{
				full = Path.GetFullPath(folder);
			}
			catch (Exception exception) when (exception is ArgumentException
				or NotSupportedException
				or PathTooLongException)
			{
				return;
			}

			if (string.Equals(
				Path.GetFileName(full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
				"steamapps",
				StringComparison.OrdinalIgnoreCase))
			{
				full = Path.GetDirectoryName(full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
					?? full;
			}

			if (!folders.Contains(full, FileSystemComparer))
			{
				folders.Add(full);
			}
		}

		Add(steamRoot);

		if (ReadFile(Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf")) is { } text
			&& ValveKeyValues.TryParse(text)?["libraryfolders"] is { } root)
		{
			foreach (var entry in root.Children.Values)
			{
				Add(entry.Value ?? entry["path"]?.Value);
			}
		}

		foreach (var extra in extraFolders)
		{
			Add(extra);
		}

		return folders;
	}

	private static StringComparer FileSystemComparer
		=> OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
			? StringComparer.OrdinalIgnoreCase
			: StringComparer.Ordinal;

	private static string Signature(string steamRoot, IReadOnlyList<string> folders)
	{
		var parts = new List<string> { Stamp(Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf")) };
		parts.AddRange(folders.Select(folder => folder + "=" + Stamp(Path.Combine(folder, "steamapps"))));
		return string.Join('|', parts);
	}

	private static string Stamp(string path)
	{
		try
		{
			return Directory.GetLastWriteTimeUtc(path).Ticks.ToString(CultureInfo.InvariantCulture)
				+ "/"
				+ File.GetLastWriteTimeUtc(path).Ticks.ToString(CultureInfo.InvariantCulture);
		}
		catch (Exception exception) when (exception is IOException
			or UnauthorizedAccessException
			or ArgumentException)
		{
			return "-";
		}
	}

	private static IReadOnlyList<SteamGame> ReadGames(IReadOnlyList<string> folders)
	{
		var games = new Dictionary<int, SteamGame>();

		foreach (var folder in folders)
		{
			var apps = Path.Combine(folder, "steamapps");
			IEnumerable<string> manifests;
			try
			{
				if (!Directory.Exists(apps))
				{
					continue;
				}

				manifests = Directory.EnumerateFiles(apps, "appmanifest_*.acf").ToList();
			}
			catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
			{
				continue;
			}

			foreach (var manifest in manifests)
			{
				if (ReadManifest(manifest) is { } game && !games.ContainsKey(game.AppId))
				{
					games[game.AppId] = game;
				}
			}
		}

		return
		[
			.. games.Values.OrderBy(game => game.Name, StringComparer.CurrentCultureIgnoreCase)
				.ThenBy(game => game.AppId)
		];
	}

	internal static SteamGame? ReadManifest(string path)
	{
		if (ReadFile(path) is not { } text || ValveKeyValues.TryParse(text)?["AppState"] is not { } state)
		{
			return null;
		}

		if (!int.TryParse(state["appid"]?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var appId)
			|| appId <= 0
			|| state["name"]?.Value is not { Length: > 0 } name)
		{
			return null;
		}

		if (!int.TryParse(state["StateFlags"]?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var flags)
			|| (flags & InstalledStateFlag) == 0)
		{
			return null;
		}

		if (_toolAppIds.Contains(appId)
			|| _toolNamePrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
		{
			return null;
		}

		DateTimeOffset? lastPlayed = long.TryParse(state["LastPlayed"]?.Value,
				NumberStyles.None,
				CultureInfo.InvariantCulture,
				out var seconds)
			&& seconds > 0
			&& seconds < 253402300799
				? DateTimeOffset.FromUnixTimeSeconds(seconds)
				: null;

		return new SteamGame(appId, name, lastPlayed, InstallDirectory(path, state["installdir"]?.Value));
	}

	internal static string? InstallDirectory(string manifestPath, string? installDir)
	{
		if (!IsPlainFolderName(installDir) || Path.GetDirectoryName(manifestPath) is not { Length: > 0 } apps)
		{
			return null;
		}

		return Path.Combine(apps, "common", installDir!);
	}

	internal static bool IsPlainFolderName(string? name)
	{
		return !string.IsNullOrWhiteSpace(name)
			&& name is not ("." or "..")
			&& name.IndexOfAny(['/', '\\', ':']) < 0
			&& !Path.IsPathRooted(name)
			&& name.Trim() == name;
	}

	internal static string? ReadFile(string path)
	{
		try
		{
			var info = new FileInfo(path);
			return info.Exists && info.Length <= MaximumFileBytes ? File.ReadAllText(path) : null;
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			return null;
		}
	}
}
