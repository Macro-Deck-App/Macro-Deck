using System.Collections.Concurrent;
using MacroDeck.Sdk;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Decks;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Notifications;
using MacroDeck.Sdk.Scripts;
using MacroDeck.Sdk.Variables;
using MacroDeck.Sdk.Widgets;
using MacroDeckHost.Integrations.Jellyfin;
using MacroDeckHost.Integrations.Jellyfin.Protocol;

namespace MacroDeckHost.Tests.UnitTests.Jellyfin;

internal sealed class FakeJellyfinClient : IJellyfinClient
{
	private volatile IReadOnlyList<JellyfinSessionDto> _sessions = [];

	public JellyfinServerSettings? Settings { get; init; }

	public bool Unreachable { get; set; }

	public bool RejectCredentials { get; set; }

	public string? IssuedToken { get; set; } = "issued-token";

	public ConcurrentQueue<string> Commands { get; } = new();

	public IReadOnlyList<JellyfinItemDto> SearchResults { get; set; } = [];

	public IReadOnlyList<JellyfinSessionDto> Sessions
	{
		get => _sessions;
		set => _sessions = value;
	}

	public Task<JellyfinPublicSystemInfo> GetPublicInfoAsync(CancellationToken cancellationToken)
	{
		ThrowIfDown();
		return Task.FromResult(new JellyfinPublicSystemInfo { ServerName = "Test", Version = "12.2.0" });
	}

	public Task VerifyTokenAsync(CancellationToken cancellationToken)
	{
		ThrowIfDown();
		return RejectCredentials ? throw new JellyfinAuthenticationException("rejected") : Task.CompletedTask;
	}

	public Task<JellyfinAuthenticationResult> AuthenticateAsync(
		string username,
		string password,
		CancellationToken cancellationToken)
	{
		ThrowIfDown();
		Commands.Enqueue($"authenticate:{username}");
		return RejectCredentials
			? throw new JellyfinAuthenticationException("rejected")
			: Task.FromResult(new JellyfinAuthenticationResult { AccessToken = IssuedToken });
	}

	public Task<IReadOnlyList<JellyfinSessionDto>> GetSessionsAsync(CancellationToken cancellationToken)
	{
		ThrowIfDown();
		return Task.FromResult(_sessions);
	}

	public Task SendPlaystateAsync(string sessionId, string command, long? seekPositionTicks,
		CancellationToken cancellationToken)
	{
		Commands.Enqueue(seekPositionTicks is { } ticks
			? $"playstate:{sessionId}:{command}:{ticks}"
			: $"playstate:{sessionId}:{command}");
		return Task.CompletedTask;
	}

	public Task SendGeneralCommandAsync(string sessionId, string command,
		IReadOnlyDictionary<string, string>? arguments, CancellationToken cancellationToken)
	{
		var args = arguments is null ? string.Empty : string.Join(',', arguments.Select(pair => $"{pair.Key}={pair.Value}"));
		Commands.Enqueue($"command:{sessionId}:{command}:{args}");
		return Task.CompletedTask;
	}

	public Task SendMessageAsync(string sessionId, string? header, string text, int? timeoutMs,
		CancellationToken cancellationToken)
	{
		Commands.Enqueue($"message:{sessionId}:{header}:{text}:{timeoutMs}");
		return Task.CompletedTask;
	}

	public Task PlayNowAsync(string sessionId, IReadOnlyList<string> itemIds, CancellationToken cancellationToken)
	{
		Commands.Enqueue($"play:{sessionId}:{string.Join(',', itemIds)}");
		return Task.CompletedTask;
	}

	public Task<IReadOnlyList<JellyfinItemDto>> SearchItemsAsync(string searchTerm, IReadOnlyList<string> itemTypes,
		string? userId, CancellationToken cancellationToken)
	{
		Commands.Enqueue($"search:{searchTerm}:{string.Join(',', itemTypes)}:{userId}");
		return Task.FromResult(SearchResults);
	}

	public Task<JellyfinImage?> GetPrimaryImageAsync(string itemId, string? tag, CancellationToken cancellationToken)
		=> Task.FromResult<JellyfinImage?>(new JellyfinImage([1, 2, 3], "image/png"));

	public Task RunSessionSocketAsync(Action<IReadOnlyList<JellyfinSessionDto>> onSessions,
		CancellationToken cancellationToken)
		=> throw new JellyfinSocketException("no socket in tests");

	private void ThrowIfDown()
	{
		if (Unreachable)
		{
			throw new HttpRequestException("unreachable");
		}
	}
}

internal sealed class JellyfinTestContext : IIntegrationContext
{
	public JellyfinTestConfig ConfigStore { get; } = new();

	public RecordingEventPublisher Published { get; } = new();

	public IIntegrationConfig Config => ConfigStore;

	public IEventPublisher Events => Published;

	public IVariableApi Variables => throw new NotSupportedException();
	public IUserVariableApi UserVariables => throw new NotSupportedException();
	public IDeckNavigator Deck => throw new NotSupportedException();
	public IScriptApi Scripts => throw new NotSupportedException();
	public IWidgetApi Widgets => throw new NotSupportedException();
	public IUserNotifier Notifications => throw new NotSupportedException();

	public Guid AddServer(string title, string url = "http://jellyfin.local:8096", string apiKey = "key",
		string? variableKey = null, string? devices = null)
		=> ConfigStore.AddEntry(title,
			new Dictionary<string, string?>
			{
				[JellyfinConfigKeys.Url] = url,
				[JellyfinConfigKeys.AuthMethod] = JellyfinConfigKeys.AuthApiKey,
				[JellyfinConfigKeys.DeviceId] = "macro-deck-device",
				[JellyfinIntegration.VariableKeyConfigKey] = variableKey,
				[JellyfinConfigKeys.Devices] = devices
			},
			new Dictionary<string, string> { [JellyfinConfigKeys.ApiKey] = apiKey });
}

internal sealed class RecordingEventPublisher : IEventPublisher
{
	public ConcurrentQueue<(string Id, IReadOnlyDictionary<string, object?> Payload)> Events { get; } = new();

	public void Publish(string eventId, IReadOnlyDictionary<string, object?>? parameters = null)
		=> Events.Enqueue((eventId, parameters ?? new Dictionary<string, object?>()));
}

internal sealed class JellyfinTestConfig : IIntegrationConfig
{
	private readonly List<ConfigEntrySnapshot> _entries = [];
	private readonly ConcurrentDictionary<(Guid EntryId, string Key), string?> _strings = new();
	private readonly ConcurrentDictionary<(Guid EntryId, string Key), string> _secrets = new();

	public Guid AddEntry(string title, IReadOnlyDictionary<string, string?> values,
		IReadOnlyDictionary<string, string> secrets)
	{
		var entryId = Guid.NewGuid();
		_entries.Add(new ConfigEntrySnapshot(entryId, title));
		foreach (var (key, value) in values)
		{
			_strings[(entryId, key)] = value;
		}

		foreach (var (key, value) in secrets)
		{
			_secrets[(entryId, key)] = value;
		}

		return entryId;
	}

	public string? Read(Guid entryId, string key) => _strings.GetValueOrDefault((entryId, key));

	public Task<IReadOnlyList<ConfigEntrySnapshot>> GetEntriesAsync(CancellationToken cancellationToken = default)
		=> Task.FromResult<IReadOnlyList<ConfigEntrySnapshot>>(_entries.ToList());

	public Task<string?> GetStringAsync(Guid entryId, string key, CancellationToken cancellationToken = default)
		=> Task.FromResult(_strings.GetValueOrDefault((entryId, key)));

	public Task<string?> GetSecretAsync(Guid entryId, string key, CancellationToken cancellationToken = default)
		=> Task.FromResult<string?>(_secrets.GetValueOrDefault((entryId, key)));

	public Task SetStringAsync(Guid entryId, string key, string? value, CancellationToken cancellationToken = default)
	{
		_strings[(entryId, key)] = value;
		return Task.CompletedTask;
	}

	public Task SetSecretAsync(Guid entryId, string key, string value, CancellationToken cancellationToken = default)
	{
		_secrets[(entryId, key)] = value;
		return Task.CompletedTask;
	}
}

internal static class JellyfinSessions
{
	public static readonly string[] AllCommands =
		["SetVolume", "VolumeUp", "VolumeDown", "Mute", "Unmute", "ToggleMute", "DisplayMessage"];

	public static JellyfinSessionDto Session(
		string deviceId,
		string deviceName = "Living Room TV",
		JellyfinItemDto? item = null,
		bool paused = false,
		bool controllable = true,
		IReadOnlyList<string>? commands = null,
		string? sessionId = null,
		long positionTicks = 0,
		DateTimeOffset? lastActivity = null)
		=> new()
		{
			Id = sessionId ?? $"session-{deviceId}",
			DeviceId = deviceId,
			DeviceName = deviceName,
			Client = "Jellyfin Web",
			UserId = "user-1",
			UserName = "alex",
			SupportsMediaControl = controllable,
			LastActivityDate = lastActivity ?? DateTimeOffset.UtcNow,
			Capabilities = new JellyfinCapabilitiesDto
			{
				SupportsMediaControl = controllable,
				SupportedCommands = commands ?? (controllable ? AllCommands : []),
				PlayableMediaTypes = ["Audio", "Video"]
			},
			NowPlayingItem = item,
			PlayState = new JellyfinPlayStateDto
			{
				IsPaused = paused,
				PositionTicks = item is null ? null : positionTicks,
				CanSeek = true,
				VolumeLevel = 40,
				PlayMethod = "DirectPlay"
			}
		};

	public static JellyfinItemDto Movie(string id = "movie-1", string name = "Big Buck Bunny")
		=> new()
		{
			Id = id,
			Name = name,
			Type = "Movie",
			MediaType = "Video",
			ProductionYear = 2008,
			RunTimeTicks = TimeSpan.FromMinutes(10).Ticks,
			ImageTags = new Dictionary<string, string> { ["Primary"] = "tag-m" }
		};

	public static JellyfinItemDto Episode()
		=> new()
		{
			Id = "episode-1",
			Name = "Pilot",
			Type = "Episode",
			MediaType = "Video",
			SeriesName = "Test Show",
			SeriesId = "series-1",
			SeriesPrimaryImageTag = "tag-s",
			SeasonName = "Season 1",
			IndexNumber = 1,
			ParentIndexNumber = 1,
			RunTimeTicks = TimeSpan.FromMinutes(20).Ticks,
			ImageTags = new Dictionary<string, string> { ["Primary"] = "tag-e" }
		};

	public static JellyfinItemDto Track()
		=> new()
		{
			Id = "track-1",
			Name = "Track 1",
			Type = "Audio",
			MediaType = "Audio",
			Album = "Test Album",
			AlbumId = "album-1",
			AlbumPrimaryImageTag = "tag-a",
			Artists = ["Test Artist"],
			RunTimeTicks = TimeSpan.FromMinutes(3).Ticks
		};

	public static JellyfinConnectionTimings FastTimings { get; } = new(TimeSpan.FromMilliseconds(20),
		TimeSpan.FromMinutes(5),
		TimeSpan.FromMilliseconds(10),
		TimeSpan.FromMilliseconds(20),
		TimeSpan.FromMilliseconds(50),
		TimeSpan.FromMilliseconds(50));
}
