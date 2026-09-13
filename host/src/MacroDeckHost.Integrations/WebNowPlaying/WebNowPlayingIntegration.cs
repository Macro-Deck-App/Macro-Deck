using System.Globalization;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Localization;
using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Issues;
using MacroDeck.Sdk.Migration;
using MacroDeck.Sdk.MusicPlayer;
using MacroDeck.Sdk.MusicPlayer.Actions;
using MacroDeck.Sdk.Variables;

namespace MacroDeckHost.Integrations.WebNowPlaying;

[MacroDeckIntegration(EnabledByDefault = false)]
public sealed class WebNowPlayingIntegration
	: IIntegration,
		IVariableProvider,
		IMusicPlayerProvider,
		IIntegrationIconProvider,
		IIntegrationIssueProvider,
		IMigrationProvider,
		IVariableRefreshSignalConsumer,
		IDisposable
{
	public const string IntegrationId = "app.macro-deck.webnowplaying";

	internal const string InstanceId = "browser";

	internal const string PortInUseIssueId = "port-in-use";

	private const string DisplayName = "WebNowPlaying";

	private const string ArtworkBaseUrl = "/api/music-player/artwork/";

	private static readonly byte[] _icon = LoadIcon();

	private readonly int _port;

	private volatile WebNowPlayingServer? _server;
	private volatile WebNowPlayingPlayers? _players;
	private volatile WebNowPlayingMusicPlayer? _player;
	private volatile bool _portInUse;
	private IVariableRefreshSignal? _refreshSignal;

	public WebNowPlayingIntegration()
		: this(WebNowPlayingServer.DefaultPort)
	{
	}

	internal WebNowPlayingIntegration(int port)
	{
		_port = port;
		Actions = MusicPlayerActions.Common(ResolvePlayer, GetInstances);
	}

	public string Id => IntegrationId;

	public LocalizedText Name => DisplayName;

	public string Version => "1.0.0";

	public bool IsInitialized { get; private set; }

	public IReadOnlyList<IActionDefinition> Actions { get; }

	public string IconMimeType => "image/png";

	public IReadOnlyList<VariableDefinition> Variables => WebNowPlayingVariables.All;

	public IReadOnlyList<IIntegrationMigration> Migrations { get; } = [new WebNowPlayingMacroDeck2Migration()];

	internal int ListeningPort => _server?.Port ?? 0;

	public byte[] GetIcon() => _icon;

	public void UseVariableRefreshSignal(IVariableRefreshSignal signal) => _refreshSignal = signal;

	public IReadOnlyList<MusicPlayerInstance> GetInstances()
		=> _player is null ? [] : [new MusicPlayerInstance(InstanceId, DisplayName)];

	public IMusicPlayer? GetPlayer(string instanceId)
		=> string.Equals(instanceId, InstanceId, StringComparison.Ordinal) ? _player : null;

	public async Task InitializeAsync(IIntegrationContext context)
	{
		await StopAsync();

		var players = new WebNowPlayingPlayers();
		var server = new WebNowPlayingServer(players, RequestRefresh);

		_portInUse = !server.TryStart(_port);
		if (_portInUse)
		{
			server.Dispose();
		}
		else
		{
			_players = players;
			_player = new WebNowPlayingMusicPlayer(players);
			_server = server;
		}

		IsInitialized = true;
		RequestRefresh();
	}

	public Task ShutdownAsync() => StopAsync();

	public void Dispose()
	{
		_server?.Dispose();
		Release();
	}

	public ValueTask<VariableReading> ReadAsync(string localId, CancellationToken cancellationToken = default)
	{
		var players = _players;

		if (string.Equals(localId, WebNowPlayingVariables.IsConnectedId, StringComparison.Ordinal))
		{
			return ValueTask.FromResult(VariableReading.Of(players?.HasConnections == true));
		}

		if (players?.Active() is not { } active)
		{
			return ValueTask.FromResult(VariableReading.Unavailable);
		}

		var player = active.Player;
		object? value = localId switch
		{
			"webnowplaying-player-name" => WebNowPlayingMusicPlayer.NullIfEmpty(player.Name),
			"webnowplaying-current-track-name" => WebNowPlayingMusicPlayer.NullIfEmpty(player.Title),
			"webnowplaying-current-artist" => WebNowPlayingMusicPlayer.NullIfEmpty(player.Artist),
			"webnowplaying-current-album" => WebNowPlayingMusicPlayer.NullIfEmpty(player.Album),
			"webnowplaying-album-art-url" => ArtworkUrl(active.ArtworkId),
			"webnowplaying-playback-state" =>
				WebNowPlayingMusicPlayer.ToPlaybackState(player.State).ToString().ToLowerInvariant(),
			"webnowplaying-is-playing" => player.State == WebNowPlayingProtocol.StatePlaying,
			WebNowPlayingVariables.CurrentPositionId => player.Position,
			"webnowplaying-track-duration" => player.Duration,
			"webnowplaying-progress-percentage" => player.Duration > 0
				? (int)Math.Clamp(player.Position * 100 / player.Duration, 0, 100)
				: null,
			WebNowPlayingVariables.VolumeId => player.Volume,
			"webnowplaying-shuffle-enabled" => player.Shuffle,
			"webnowplaying-repeat-mode" => WebNowPlayingMusicPlayer.ToRepeatMode(player.Repeat).ToString()
				.ToLowerInvariant(),
			"webnowplaying-rating" => player.Rating,
			_ => null
		};

		return ValueTask.FromResult(localId switch
		{
			WebNowPlayingVariables.VolumeId => VariableReading.Of(value, 0, 100, 1),
			WebNowPlayingVariables.CurrentPositionId => VariableReading.Of(value,
				0,
				player.Duration > 0 ? player.Duration : null,
				1),
			_ => VariableReading.Of(value)
		});
	}

	public ValueTask<VariableWriteResult> SetValueAsync(
		string localId,
		object? value,
		CancellationToken cancellationToken = default)
	{
		var player = _players?.Active() is null ? null : _player;

		return localId switch
		{
			WebNowPlayingVariables.VolumeId =>
				MusicPlayerVariableWrites.SetVolumeAsync(player, value, cancellationToken),
			WebNowPlayingVariables.CurrentPositionId =>
				MusicPlayerVariableWrites.SeekAsync(player, value, cancellationToken),
			_ => ValueTask.FromResult(VariableWriteResult.NotWritable())
		};
	}

	public Task<IReadOnlyList<IntegrationIssue>> GetIssuesAsync(CancellationToken cancellationToken = default)
	{
		IReadOnlyList<IntegrationIssue> issues = _portInUse
			?
			[
				new IntegrationIssue
				{
					Id = PortInUseIssueId,
					Title = AppStrings.Integrations.WebNowPlaying.Issues.PortInUseTitle(),
					Description = AppStrings.Integrations.WebNowPlaying.Issues.PortInUseDescription(
						port: _port.ToString(CultureInfo.InvariantCulture)),
					Severity = IntegrationIssueSeverity.Error
				}
			]
			: [];

		return Task.FromResult(issues);
	}

	public Task<IssueResolution> ResolveIssueAsync(string issueId, CancellationToken cancellationToken = default)
		=> Task.FromResult(IssueResolution.Failed(AppStrings.Integrations.Issues.UnknownIssue()));

	private IMusicPlayer? ResolvePlayer(string? instanceId)
		=> string.IsNullOrEmpty(instanceId) ? _player : GetPlayer(instanceId);

	private static string? ArtworkUrl(string? artworkId)
		=> artworkId is null
			? null
			: ArtworkBaseUrl +
			Uri.EscapeDataString(artworkId) +
			"?instanceId=" +
			Uri.EscapeDataString($"{IntegrationId}::{InstanceId}");

	private void RequestRefresh() => _refreshSignal?.RequestEagerRefresh(IntegrationId);

	private async Task StopAsync()
	{
		var server = _server;
		Release();

		if (server is not null)
		{
			await server.StopAsync();
		}

		RequestRefresh();
	}

	private void Release()
	{
		_server = null;
		_player = null;
		_players = null;
		_portInUse = false;
	}

	private static byte[] LoadIcon()
	{
		var assembly = typeof(WebNowPlayingIntegration).Assembly;
		var name = assembly.GetManifestResourceNames()
			.First(n => n.EndsWith("webnowplaying-icon.png", StringComparison.Ordinal));
		using var stream = assembly.GetManifestResourceStream(name)!;
		using var memory = new MemoryStream();
		stream.CopyTo(memory);
		return memory.ToArray();
	}
}
