using System.Globalization;
using MacroDeck.Sdk.Events;
using MacroDeckHost.Integrations.Steam.Native;
using MacroDeckHost.Integrations.Steam.Protocol;
using MacroDeckHost.Integrations.Steam.State;

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

	public string? ReadRegistrySteamPath()
	{
		return RegistrySteamPath;
	}

	public int? ReadRegistryRunningAppId()
	{
		return RegistryRunningAppId;
	}

	public bool IsSteamProcessRunning()
	{
		return SteamProcessRunning;
	}

	public IReadOnlyList<int> FindLaunchedAppIds()
	{
		return LaunchedAppIds;
	}

	public bool CanCloseGames { get; set; } = true;

	public List<SteamGameProcess> GameProcesses { get; } = [];

	public HashSet<int> IgnoresCloseRequests { get; } = [];

	public HashSet<int> WithoutWindow { get; } = [];

	public List<(int AppId, string? InstallDirectory)> ProcessQueries { get; } = [];

	public List<int> CloseRequests { get; } = [];

	public List<int> Killed { get; } = [];

	public IReadOnlyList<SteamGameProcess> FindGameProcesses(int appId, string? installDirectory)
	{
		ProcessQueries.Add((appId, installDirectory));
		return [.. GameProcesses];
	}

	public bool RequestClose(SteamGameProcess process)
	{
		CloseRequests.Add(process.Id);
		if (WithoutWindow.Contains(process.Id))
		{
			return false;
		}

		if (!IgnoresCloseRequests.Contains(process.Id))
		{
			GameProcesses.Remove(process);
		}

		return true;
	}

	public bool Kill(SteamGameProcess process)
	{
		Killed.Add(process.Id);
		GameProcesses.Remove(process);
		return true;
	}

	public bool IsRunning(SteamGameProcess process)
	{
		return GameProcesses.Contains(process);
	}
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
		long? lastPlayed = null,
		string? installDir = null)
	{
		var text = $$"""
					 "AppState"
					 {
					 	"appid"		"{{appId.ToString(CultureInfo.InvariantCulture)}}"
					 	"name"		"{{name}}"
					 	"StateFlags"		"{{stateFlags.ToString(CultureInfo.InvariantCulture)}}"
					 	"LastPlayed"		"{{(lastPlayed ?? 0).ToString(CultureInfo.InvariantCulture)}}"
					 	"installdir"		"{{installDir ?? string.Empty}}"
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
			Directory.Delete(Root, true);
		}
		catch (IOException)
		{
		}
		catch (UnauthorizedAccessException)
		{
		}
	}
}

internal sealed class RecordingEventPublisher : IEventPublisher
{
	private readonly Lock _sync = new();
	private readonly List<(string EventId, IReadOnlyDictionary<string, object?> Payload)> _published = [];

	public IReadOnlyList<(string EventId, IReadOnlyDictionary<string, object?> Payload)> Published
	{
		get
		{
			lock (_sync)
			{
				return [.. _published];
			}
		}
	}

	public void Publish(string eventId, IReadOnlyDictionary<string, object?>? parameters = null)
	{
		lock (_sync)
		{
			_published.Add((eventId, parameters ?? new Dictionary<string, object?>()));
		}
	}
}

internal sealed class ScriptedWebApi : ISteamWebApiClient
{
	public SteamPlayerSummary? Self { get; set; }

	public SteamWebFailure? SelfFailure { get; set; }

	public IReadOnlyList<SteamPlayerSummary> Friends { get; set; } = [];

	public SteamWebFailure? FriendsFailure { get; set; }

	public Task<SteamPlayerSummary?> GetPlayerSummaryAsync(string steamId, CancellationToken cancellationToken)
	{
		return SelfFailure is { } failure ? throw new SteamWebApiException(failure) : Task.FromResult(Self);
	}

	public Task<IReadOnlyList<SteamPlayerSummary>> GetPlayerSummariesAsync(
		IReadOnlyList<string> steamIds,
		CancellationToken cancellationToken)
	{
		return Task.FromResult(Friends);
	}

	public Task<IReadOnlyList<string>?> GetFriendIdsAsync(string steamId, CancellationToken cancellationToken)
	{
		return FriendsFailure is { } failure
			? throw new SteamWebApiException(failure)
			: Task.FromResult<IReadOnlyList<string>?>([.. Friends.Select(friend => friend.SteamId)]);
	}

	public Task<string?> ResolveVanityUrlAsync(string vanityName, CancellationToken cancellationToken)
	{
		return Task.FromResult<string?>(null);
	}

	public IReadOnlyList<SteamOwnedGame>? OwnedGames { get; set; } = [];

	public SteamWebFailure? OwnedFailure { get; set; }

	public Task<IReadOnlyList<SteamOwnedGame>?> GetOwnedGamesAsync(string steamId, CancellationToken cancellationToken)
	{
		return OwnedFailure is { } failure ? throw new SteamWebApiException(failure) : Task.FromResult(OwnedGames);
	}
}
