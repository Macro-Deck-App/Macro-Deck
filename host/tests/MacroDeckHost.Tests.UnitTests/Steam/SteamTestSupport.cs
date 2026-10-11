using System.Globalization;
using MacroDeckHost.Integrations.Steam;

namespace MacroDeckHost.Tests.UnitTests.Steam;

internal sealed class FakeSteamPlatform : ISteamPlatform
{
	public SteamOs Os { get; set; } = SteamOs.Linux;

	public string HomeDirectory { get; set; } = string.Empty;

	public IReadOnlyList<string> ProgramFilesDirectories { get; set; } = [];

	public string? RegistrySteamPath { get; set; }

	public int? RegistryRunningAppId { get; set; }

	public bool SteamProcessRunning { get; set; } = true;

	public List<int> LaunchedAppIds { get; } = [];

	public string? ReadRegistrySteamPath() => RegistrySteamPath;

	public int? ReadRegistryRunningAppId() => RegistryRunningAppId;

	public bool IsSteamProcessRunning() => SteamProcessRunning;

	public IReadOnlyList<int> FindLaunchedAppIds() => LaunchedAppIds;
}

internal sealed class SteamFolders : IDisposable
{
	public SteamFolders()
	{
		Root = Path.Combine(Path.GetTempPath(), "md-steam-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(Root);
	}

	public string Root { get; }

	public string Make(string name)
	{
		var path = Path.Combine(Root, name);
		Directory.CreateDirectory(Path.Combine(path, "steamapps"));
		return path;
	}

	public static void WriteManifest(
		string library,
		int appId,
		string name,
		int stateFlags = 4,
		long? lastPlayed = null)
	{
		var text = $$"""
			"AppState"
			{
				"appid"		"{{appId.ToString(CultureInfo.InvariantCulture)}}"
				"name"		"{{name}}"
				"StateFlags"		"{{stateFlags.ToString(CultureInfo.InvariantCulture)}}"
				"LastPlayed"		"{{(lastPlayed ?? 0).ToString(CultureInfo.InvariantCulture)}}"
			}
			""";
		File.WriteAllText(Path.Combine(library, "steamapps", $"appmanifest_{appId}.acf"), text);
	}

	public static void WriteLibraryFolders(string steamRoot, params string[] others)
	{
		var entries = string.Concat(others.Select((path, index) => $$"""
				"{{index + 1}}"
				{
					"path"		"{{path.Replace("\\", "\\\\", StringComparison.Ordinal)}}"
					"label"		""
				}

			"""));
		File.WriteAllText(Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf"),
			$$"""
			"libraryfolders"
			{
				"0"
				{
					"path"		"{{steamRoot.Replace("\\", "\\\\", StringComparison.Ordinal)}}"
				}
			{{entries}}}
			""");
	}

	public void Dispose()
	{
		try
		{
			Directory.Delete(Root, recursive: true);
		}
		catch (IOException)
		{
		}
		catch (UnauthorizedAccessException)
		{
		}
	}
}
