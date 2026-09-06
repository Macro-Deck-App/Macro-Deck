using System.Globalization;
using MacroDeckHost.Integrations.YtmDesktop.Actions;
using MacroDeckHost.Integrations.YtmDesktop.Protocol;
using MacroDeckHost.Localization;
using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Issues;
using MacroDeck.Sdk.Logging;
using MacroDeck.Sdk.Migration;
using MacroDeck.Sdk.MusicPlayer;
using MacroDeck.Sdk.MusicPlayer.Actions;
using MacroDeck.Sdk.Variables;
using Serilog;

namespace MacroDeckHost.Integrations.YtmDesktop;

[MacroDeckIntegration]
public sealed class YtmDesktopIntegration
	: IIntegration,
		IConfigFlowProvider,
		IVariableProvider,
		IMusicPlayerProvider,
		IEventProvider,
		IIntegrationIconProvider,
		IIntegrationIssueProvider,
		IMigrationProvider,
		IDisposable
{
	public const string IntegrationId = "app.macro-deck.ytmdesktop";

	public const string IntegrationVersion = "1.0.0";

	internal const string AuthorizationIssueId = "authorization-revoked";

	private const string ArtworkBaseUrl = "/api/music-player/artwork/";

	private static readonly ILogger _logger = IntegrationLog.For<YtmDesktopIntegration>(IntegrationId);
	private static readonly byte[] _icon = LoadIcon();

	private readonly YtmDesktopMusicPlayer _player = new();

	private YtmDesktopConnection? _connection;
	private string? _entryId;

	public YtmDesktopIntegration()
	{
		Actions =
		[
			.. MusicPlayerActions.Common(ResolvePlayer, GetInstances),
			MusicPlayerActions.PlayTrack(Id, ResolvePlayer, GetInstances),
			MusicPlayerActions.PlayPlaylist(Id, ResolvePlayer, GetInstances),
			.. YtmDesktopActions.Create(ResolvePlayer, GetInstances)
		];
	}

	public string Id => IntegrationId;

	public LocalizedText Name => "YouTube Music Desktop App";

	public string Version => IntegrationVersion;

	public bool IsInitialized { get; private set; }

	public IReadOnlyList<IActionDefinition> Actions { get; }

	public string IconMimeType => "image/svg+xml";

	public IReadOnlyList<VariableDefinition> Variables => YtmDesktopVariables.All;

	public IReadOnlyList<EventDefinition> EventDefinitions => YtmDesktopEventDefinitions.All;

	public bool AllowsMultipleConfigurations => false;

	public IReadOnlyList<IIntegrationMigration> Migrations { get; } = [new YtmDesktopMacroDeck2Migration()];

	public byte[] GetIcon() => _icon;

	public IConfigFlow CreateConfigFlow() => new YtmDesktopConfigFlow();

	public IReadOnlyList<MusicPlayerInstance> GetInstances()
		=> _entryId is { } entryId ? [new MusicPlayerInstance(entryId, "YouTube Music Desktop App")] : [];

	public IMusicPlayer? GetPlayer(string instanceId)
		=> string.Equals(instanceId, _entryId, StringComparison.Ordinal) ? _player : null;

	public async Task InitializeAsync(IIntegrationContext context)
	{
		await ConnectFromConfig(context);
		IsInitialized = true;
	}

	public Task ShutdownAsync() => DisconnectAsync();

	public void Dispose() => Disconnect();

	public ValueTask<VariableReading> ReadAsync(string localId, CancellationToken cancellationToken = default)
	{
		var snapshot = _connection?.Snapshot ?? YtmDesktopSnapshot.Disconnected;
		var state = snapshot.Player;

		if (string.Equals(localId, YtmDesktopVariables.IsConnectedId, StringComparison.Ordinal))
		{
			return ValueTask.FromResult(VariableReading.Of(state.IsConnected));
		}

		if (!state.IsConnected)
		{
			return ValueTask.FromResult(VariableReading.Unavailable);
		}

		object? value = localId switch
		{
			"ytmdesktop-current-track-name" => state.TrackName,
			"ytmdesktop-current-artist" => state.Artists.Count > 0 ? string.Join(", ", state.Artists) : null,
			"ytmdesktop-current-album" => state.AlbumName,
			"ytmdesktop-album-art-url" => ArtworkUrl(state.ArtworkId),
			"ytmdesktop-playback-state" => state.PlaybackState.ToString().ToLowerInvariant(),
			"ytmdesktop-is-playing" => state.PlaybackState == PlaybackState.Playing,
			YtmDesktopVariables.CurrentPositionId => state.Position is { } position
				? (int)position.TotalSeconds
				: null,
			"ytmdesktop-track-duration" => state.Duration is { } duration ? (int)duration.TotalSeconds : null,
			"ytmdesktop-progress-percentage" => Progress(state),
			YtmDesktopVariables.VolumeId => state.VolumePercent,
			"ytmdesktop-is-muted" => snapshot.Muted,
			"ytmdesktop-like-status" => snapshot.LikeStatus is { } like ? YtmDesktopEventEmitter.Describe(like) : null,
			"ytmdesktop-is-liked" => snapshot.LikeStatus is { } liked ? liked == YtmLikeStatus.Like : null,
			"ytmdesktop-shuffle-enabled" => snapshot.ShuffleEnabled,
			"ytmdesktop-repeat-mode" => state.RepeatMode.ToString().ToLowerInvariant(),
			"ytmdesktop-is-live" => snapshot.IsLive,
			"ytmdesktop-media-type" => MediaType(snapshot.VideoType),
			"ytmdesktop-ad-playing" => snapshot.AdPlaying,
			"ytmdesktop-video-id" => snapshot.VideoId,
			"ytmdesktop-device-name" => state.DeviceName,
			_ => null
		};

		return ValueTask.FromResult(localId switch
		{
			YtmDesktopVariables.VolumeId => VariableReading.Of(value, 0, 100, 1),
			// The seek range is the current track's length, which is exactly why bounds ride on the
			// reading rather than on the declaration.
			YtmDesktopVariables.CurrentPositionId => VariableReading.Of(value,
				0,
				state.Duration is { } length ? (int)length.TotalSeconds : null,
				1),
			_ => VariableReading.Of(value)
		});
	}

	public ValueTask<VariableWriteResult> SetValueAsync(
		string localId,
		object? value,
		CancellationToken cancellationToken = default)
	{
		var player = _connection?.Snapshot.Player.IsConnected == true ? _player : null;

		return localId switch
		{
			YtmDesktopVariables.VolumeId =>
				MusicPlayerVariableWrites.SetVolumeAsync(player, value, cancellationToken),
			YtmDesktopVariables.CurrentPositionId =>
				MusicPlayerVariableWrites.SeekAsync(player, value, cancellationToken),
			_ => ValueTask.FromResult(VariableWriteResult.NotWritable())
		};
	}

	public Task<IReadOnlyList<IntegrationIssue>> GetIssuesAsync(CancellationToken cancellationToken = default)
	{
		// Cheap by contract: this is polled to render a badge, so it only reads a flag. "The app is not
		// running" is deliberately not an issue - it is the normal state of a long-lived host, it
		// recovers on its own, and ytmdesktop_is_connected already says so.
		IReadOnlyList<IntegrationIssue> issues = _connection?.NeedsAuthorization == true
			?
			[
				new IntegrationIssue
				{
					Id = AuthorizationIssueId,
					Title = AppStrings.Integrations.YtmDesktop.Issues.AuthorizationRejectedTitle(),
					Description = AppStrings.Integrations.YtmDesktop.Issues.AuthorizationRejectedDescription(),
					Severity = IntegrationIssueSeverity.Error,
					ActionLabel = AppStrings.Integrations.YtmDesktop.Issues.OpenSetupAction()
				}
			]
			: [];

		return Task.FromResult(issues);
	}

	public Task<IssueResolution> ResolveIssueAsync(string issueId, CancellationToken cancellationToken = default)
		=> Task.FromResult(issueId == AuthorizationIssueId
			? IssueResolution.Ok(followUp: IssueResolutionFollowUp.StartConfigFlow)
			: IssueResolution.Failed(AppStrings.Integrations.Issues.UnknownIssue()));

	private IMusicPlayer? ResolvePlayer(string? instanceId)
		=> _entryId is null
			? null
			: string.IsNullOrEmpty(instanceId)
				? _player
				: GetPlayer(instanceId);

	private string? ArtworkUrl(string? artworkId)
	{
		if (artworkId is null || _entryId is not { } entryId)
		{
			return null;
		}

		return ArtworkBaseUrl +
			Uri.EscapeDataString(artworkId) +
			"?instanceId=" +
			Uri.EscapeDataString($"{Id}::{entryId}");
	}

	private static int? Progress(MusicPlayerState state)
	{
		if (state.Duration is not { } duration || duration.TotalMilliseconds <= 0 || state.Position is not { } position)
		{
			return null;
		}

		return (int)Math.Clamp(position.TotalMilliseconds / duration.TotalMilliseconds * 100, 0, 100);
	}

	private static string? MediaType(YtmVideoType? videoType)
		=> videoType switch
		{
			YtmVideoType.Audio => "audio",
			YtmVideoType.Video => "video",
			YtmVideoType.Uploaded => "uploaded",
			YtmVideoType.Podcast => "podcast",
			_ => null
		};

	private async Task DisconnectAsync()
	{
		if (_connection is { } connection)
		{
			_connection = null;
			await connection.StopAsync();
		}

		_player.Disconnect();
		_entryId = null;
	}

	private void Disconnect()
	{
		_connection?.Dispose();
		_connection = null;
		_player.Disconnect();
		_entryId = null;
	}

	private async Task ConnectFromConfig(IIntegrationContext context)
	{
		await DisconnectAsync();

		var entries = await context.Config.GetEntriesAsync();
		if (entries.Count == 0)
		{
			return;
		}

		var entry = entries[0];
		var host = await context.Config.GetStringAsync(entry.Id, YtmDesktopConfigKeys.Host);
		var port = await context.Config.GetStringAsync(entry.Id, YtmDesktopConfigKeys.Port);
		var token = await context.Config.GetSecretAsync(entry.Id, YtmDesktopConfigKeys.Token);

		if (string.IsNullOrEmpty(token))
		{
			_logger.Warning("YouTube Music Desktop App config entry {EntryId} has no token; skipping", entry.Id);
			return;
		}

		if (!int.TryParse(port, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedPort))
		{
			parsedPort = YtmDesktopEndpoint.DefaultPort;
		}

		var endpoint = YtmDesktopEndpoint.Create(host, parsedPort);
		var events = new YtmDesktopEventEmitter(context.Events);

		var connection = new YtmDesktopConnection(() => new YtmDesktopRealtimeClient(),
			() => new YtmDesktopApiClient(endpoint),
			endpoint,
			token,
			events,
			registerArtwork: _player.RegisterArtwork);

		_entryId = entry.Id.ToString();
		_connection = connection;
		_player.Connect(connection);
		connection.Start();

		_logger.Information("YouTube Music Desktop App configured for {Address}", endpoint.DisplayAddress);
	}

	private static byte[] LoadIcon()
	{
		var assembly = typeof(YtmDesktopIntegration).Assembly;
		var name = assembly.GetManifestResourceNames()
			.First(n => n.EndsWith("ytmdesktop-icon.svg", StringComparison.Ordinal));
		using var stream = assembly.GetManifestResourceStream(name)!;
		using var memory = new MemoryStream();
		stream.CopyTo(memory);
		return memory.ToArray();
	}
}
