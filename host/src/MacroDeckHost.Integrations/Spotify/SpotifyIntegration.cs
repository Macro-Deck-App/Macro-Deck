using System.Globalization;
using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Issues;
using MacroDeck.Sdk.Logging;
using MacroDeck.Sdk.Migration;
using MacroDeck.Sdk.MusicPlayer;
using MacroDeck.Sdk.MusicPlayer.Actions;
using MacroDeck.Sdk.Variables;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Localization;
using Serilog;
using SpotifyAPI.Web;
using SpotifyAPI.Web.Http;

namespace MacroDeckHost.Integrations.Spotify;

[MacroDeckIntegration]
public sealed class SpotifyIntegration
	: IIntegration, IVariableProvider, IConfigFlowProvider, IMusicPlayerProvider, IIntegrationIconProvider,
		IIntegrationIssueProvider, IMigrationProvider, IDisposable
{
	public const string IntegrationId = "app.macro-deck.spotify";

	private const string ArtworkBaseUrl = "/api/music-player/artwork/";
	private const string SpotifyWebApiDocsUrl = "https://developer.spotify.com/documentation/web-api";
	private const string PercentUnit = "%";
	private const string VolumeId = "spotify-volume";
	private const string CurrentPositionId = "spotify-current-position";
	private const string CredentialsIssueId = "credentials-invalid";
	private const string ScopesOutdatedIssueId = "scopes-outdated";
	private const string PremiumRequiredIssueId = "premium-required";
	private const string ApiLimitIssueId = "api-limited";
	private const string UnreachableIssueId = "temporarily-unreachable";

	private static readonly ILogger _logger = IntegrationLog.For<SpotifyIntegration>(IntegrationId);
	private static readonly byte[] _icon = LoadIcon();

	private static readonly TimeSpan _tokenDrainTimeout = TimeSpan.FromSeconds(10);

	private static readonly TimeSpan _defaultPersisterDrainTimeout = TimeSpan.FromSeconds(10);

	private static readonly TimeSpan _lifecycleGateTimeout = TimeSpan.FromSeconds(30);

	// TemporarilyUnavailable stays issue-free below this so a network blink never raises a banner; a
	// longer outage surfaces as informational, because a silently frozen widget reads as "broken" (#298).
	private static readonly TimeSpan _defaultUnreachableIssueThreshold = TimeSpan.FromMinutes(2);

	private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
	private readonly ISpotifyOAuthClient _oauth;
	private readonly IHTTPClient _transport;
	private readonly TimeSpan _unreachableIssueThreshold;
	private readonly TimeSpan _persisterDrainTimeout;
	private readonly TimeSpan? _persistenceBudget;
	private readonly TimeProvider? _timeProvider;
	private readonly Func<Task<TimeOfDayFormat>>? _timeOfDayFormat;

	private IIntegrationContext? _context;
	private SpotifyTokenPersister? _tokenPersister;
	private volatile List<Instance> _instances = [];
	private volatile bool _scopesOutdated;
	private bool _issuePollSilencedNotInitializedLogged;
	private bool _issuePollSilencedNoEntriesLogged;

	public string Id => IntegrationId;
	public LocalizedText Name => "Spotify";
	public string Version => "1.0.0";
	public bool IsInitialized { get; private set; }

	public IReadOnlyList<IActionDefinition> Actions { get; }

	// Built-in integrations are created by Activator.CreateInstance and never see the DI container,
	// so the host installs its time-of-day resolution here once it is built.
	internal static Func<Task<TimeOfDayFormat>>? HostTimeOfDayFormat { get; set; }

	public SpotifyIntegration()
		: this(new SpotifyOAuthClient())
	{
	}

	// The transport is only injected by tests, which must not reach api.spotify.com.
	internal SpotifyIntegration(ISpotifyOAuthClient oauth,
		IHTTPClient? transport = null,
		TimeSpan? unreachableIssueThreshold = null,
		TimeSpan? persisterDrainTimeout = null,
		TimeSpan? persistenceBudget = null,
		TimeProvider? timeProvider = null,
		Func<Task<TimeOfDayFormat>>? timeOfDayFormat = null)
	{
		_oauth = oauth;
		_timeProvider = timeProvider;
		_timeOfDayFormat = timeOfDayFormat;
		_transport = transport ?? SpotifyHttpClients.Api;
		_unreachableIssueThreshold = unreachableIssueThreshold ?? _defaultUnreachableIssueThreshold;
		_persisterDrainTimeout = persisterDrainTimeout ?? _defaultPersisterDrainTimeout;
		_persistenceBudget = persistenceBudget;
		var commonActions = MusicPlayerActions.Common(ResolvePlayer, GetInstances);
		var commonRefreshState = commonActions.Single(action => action.Id == "refresh-state");
		Actions =
		[
			.. commonActions.Where(action => action.Id != "refresh-state"),
			new MusicPlayerActionDefinition(ResolvePlayer,
				GetInstances,
				"refresh-state",
				commonRefreshState.Name,
				commonRefreshState.Description,
				[],
				(player, _, _, cancellationToken) => player is SpotifyMusicPlayer spotify
					? spotify.RefreshStateAsync(cancellationToken)
					: player.GetStateAsync(cancellationToken)),
			MusicPlayerActions.PlayTrack(Id, ResolvePlayer, GetInstances),
			MusicPlayerActions.PlayPlaylist(Id, ResolvePlayer, GetInstances),
			MusicPlayerActions.PlayOnDevice(Id, ResolvePlayer, GetInstances),
			MusicPlayerActions.TransferPlayback(Id, ResolvePlayer, GetInstances),
			.. SpotifyLibraryActions.All(ResolvePlayer, GetInstances)
		];
	}

	public string IconMimeType => "image/svg+xml";

	private SpotifyMusicPlayer? DefaultPlayer
	{
		get
		{
			var instances = _instances;
			return instances.Count > 0 ? instances[0].Player : null;
		}
	}

	public IReadOnlyList<VariableDefinition> Variables { get; } =
	[
		VariableDefinition.Eager("spotify_current_track_name",
				VariableType.Text,
				refreshInterval: TimeSpan.FromSeconds(2))
			with
			{
				DisplayName = AppStrings.Integrations.Spotify.Variables.CurrentTrackName()
			},
		VariableDefinition.Eager("spotify_current_artist", VariableType.Text, refreshInterval: TimeSpan.FromSeconds(2))
			with
			{
				DisplayName = AppStrings.Integrations.Spotify.Variables.CurrentArtist()
			},
		VariableDefinition.Eager("spotify_current_album", VariableType.Text, refreshInterval: TimeSpan.FromSeconds(2))
			with
			{
				DisplayName = AppStrings.Integrations.Spotify.Variables.CurrentAlbum()
			},
		VariableDefinition.Eager("spotify_playback_state", VariableType.Text, refreshInterval: TimeSpan.FromSeconds(2))
			with
			{
				DisplayName = AppStrings.Integrations.Spotify.Variables.PlaybackState()
			},
		VariableDefinition.Eager("spotify_is_playing", VariableType.Boolean, refreshInterval: TimeSpan.FromSeconds(2))
			with
			{
				DisplayName = AppStrings.Integrations.Spotify.Variables.IsPlaying()
			},
		VariableDefinition.Eager("spotify_volume", VariableType.Numeric, 0, TimeSpan.FromSeconds(2))
			with
			{
				DisplayName = AppStrings.Integrations.Spotify.Variables.Volume(),
				Unit = PercentUnit,
				SemanticKind = VariableSemanticKinds.Percentage,
				Write = MusicPlayerVariableWrites.Volume
			},
		VariableDefinition.Eager("spotify_track_duration", VariableType.Numeric, 0, TimeSpan.FromSeconds(2))
			with
			{
				DisplayName = AppStrings.Integrations.Spotify.Variables.TrackDuration(),
				SemanticKind = VariableSemanticKinds.Duration
			},
		VariableDefinition.Eager("spotify_current_position", VariableType.Numeric, 0, TimeSpan.FromSeconds(1))
			with
			{
				DisplayName = AppStrings.Integrations.Spotify.Variables.CurrentPosition(),
				SemanticKind = VariableSemanticKinds.Duration,
				Write = MusicPlayerVariableWrites.Position
			},
		VariableDefinition.Eager("spotify_progress_percentage", VariableType.Numeric, 0, TimeSpan.FromSeconds(1))
			with
			{
				DisplayName = AppStrings.Integrations.Spotify.Variables.ProgressPercentage()
			},
		VariableDefinition.Eager("spotify_album_art_url", VariableType.Text, refreshInterval: TimeSpan.FromSeconds(2))
			with
			{
				DisplayName = AppStrings.Integrations.Spotify.Variables.AlbumArtUrl()
			},
		VariableDefinition.Eager("spotify_device_name", VariableType.Text, refreshInterval: TimeSpan.FromSeconds(5))
			with
			{
				DisplayName = AppStrings.Integrations.Spotify.Variables.DeviceName()
			},
		VariableDefinition.Eager("spotify_device_type", VariableType.Text, refreshInterval: TimeSpan.FromSeconds(5))
			with
			{
				DisplayName = AppStrings.Integrations.Spotify.Variables.DeviceType()
			},
		VariableDefinition.Eager("spotify_shuffle_enabled",
				VariableType.Boolean,
				refreshInterval: TimeSpan.FromSeconds(5))
			with
			{
				DisplayName = AppStrings.Integrations.Spotify.Variables.ShuffleEnabled()
			},
		VariableDefinition.Eager("spotify_repeat_mode", VariableType.Text, refreshInterval: TimeSpan.FromSeconds(5))
			with
			{
				DisplayName = AppStrings.Integrations.Spotify.Variables.RepeatMode()
			},
		VariableDefinition.Eager("spotify_is_connected", VariableType.Boolean, refreshInterval: TimeSpan.FromSeconds(5))
			with
			{
				DisplayName = AppStrings.Integrations.Spotify.Variables.IsConnected()
			},
		VariableDefinition.Eager("spotify_current_track_url",
				VariableType.Text,
				refreshInterval: TimeSpan.FromSeconds(2))
			with
			{
				DisplayName = AppStrings.Integrations.Spotify.Variables.CurrentTrackUrl()
			},
		VariableDefinition.Eager("spotify_current_track_is_liked",
				VariableType.Boolean,
				refreshInterval: TimeSpan.FromSeconds(5))
			with
			{
				DisplayName = AppStrings.Integrations.Spotify.Variables.CurrentTrackIsLiked()
			},
		VariableDefinition.Eager("spotify_top_track", VariableType.Text, refreshInterval: TimeSpan.FromMinutes(1))
			with
			{
				DisplayName = AppStrings.Integrations.Spotify.Variables.TopTrack()
			},
		VariableDefinition.Eager("spotify_top_artist", VariableType.Text, refreshInterval: TimeSpan.FromMinutes(1))
			with
			{
				DisplayName = AppStrings.Integrations.Spotify.Variables.TopArtist()
			},
		VariableDefinition.Eager("spotify_top_tracks", VariableType.Text, refreshInterval: TimeSpan.FromMinutes(1))
			with
			{
				DisplayName = AppStrings.Integrations.Spotify.Variables.TopTracks()
			},
		VariableDefinition.Eager("spotify_top_artists", VariableType.Text, refreshInterval: TimeSpan.FromMinutes(1))
			with
			{
				DisplayName = AppStrings.Integrations.Spotify.Variables.TopArtists()
			}
	];

	public byte[] GetIcon() => _icon;

	public IConfigFlow CreateConfigFlow() => new SpotifyConfigFlow();

	public bool AllowsMultipleConfigurations => false;

	public IReadOnlyList<IIntegrationMigration> Migrations { get; } = [new SpotifyMacroDeck2Migration()];

	public IReadOnlyList<MusicPlayerInstance> GetInstances()
		=> _instances.Select(i => new MusicPlayerInstance(i.EntryId, i.Title)).ToList();

	public IMusicPlayer? GetPlayer(string instanceId)
		=> _instances.FirstOrDefault(i => i.EntryId == instanceId)?.Player;

	private IMusicPlayer? ResolvePlayer(string? instanceId)
		=> string.IsNullOrEmpty(instanceId) ? DefaultPlayer : GetPlayer(instanceId);

	public async Task InitializeAsync(IIntegrationContext context)
	{
		if (!await TryEnterLifecycleGateAsync("initialization"))
		{
			throw new TimeoutException($"Spotify could not start within {_lifecycleGateTimeout}.");
		}

		try
		{
			// Be defensive against overlapping lifecycle requests and an initialization retry after a
			// partial failure. IntegrationLifecycle normally shuts down first, but UI requests can race.
			if (_tokenPersister is not null || _instances.Count > 0)
			{
				await ShutdownCoreAsync();
			}

			_context = context;
			var tokenPersister = new SpotifyTokenPersister(context.Config, _logger);
			_tokenPersister = tokenPersister;

			try
			{
				await ConnectFromConfig(context, tokenPersister);
				IsInitialized = true;
			}
			catch
			{
				_tokenPersister = null;
				try
				{
					await tokenPersister.CompleteAsync().WaitAsync(_persisterDrainTimeout);
				}
				catch (Exception persistenceError)
				{
					_logger.Error(persistenceError,
						"Failed to drain Spotify token persistence after initialization failed");
				}
				finally
				{
					tokenPersister.Dispose();
				}

				throw;
			}
		}
		finally
		{
			_lifecycleGate.Release();
		}
	}

	public async Task ShutdownAsync()
	{
		if (!await TryEnterLifecycleGateAsync("shutdown"))
		{
			throw new TimeoutException($"Spotify could not shut down within {_lifecycleGateTimeout}.");
		}

		try
		{
			await ShutdownCoreAsync();
		}
		finally
		{
			_lifecycleGate.Release();
		}
	}

	private async Task<bool> TryEnterLifecycleGateAsync(string operation)
	{
		if (await _lifecycleGate.WaitAsync(_lifecycleGateTimeout))
		{
			return true;
		}

		_logger.Warning("Spotify {Operation} gave up waiting {Timeout} for the lifecycle gate",
			operation,
			_lifecycleGateTimeout);
		return false;
	}

	private async Task ShutdownCoreAsync()
	{
		var instances = _instances;
		_instances = [];
		_scopesOutdated = false;
		var tokenPersister = _tokenPersister;
		_tokenPersister = null;
		IsInitialized = false;

		try
		{
			await StopInstancesAsync(instances);
		}
		finally
		{
			if (tokenPersister is not null)
			{
				try
				{
					await tokenPersister.CompleteAsync().WaitAsync(_persisterDrainTimeout);
				}
				catch (TimeoutException)
				{
					_logger.Error(
						"Spotify token persistence did not drain within {Timeout} during shutdown; a rotated " +
						"refresh token may not have been stored",
						_persisterDrainTimeout);
				}
				catch (Exception ex)
				{
					_logger.Error(ex, "Spotify token persistence failed during shutdown");
				}
				finally
				{
					tokenPersister.Dispose();
				}
			}
		}
	}

	public void Dispose()
	{
		if (!_lifecycleGate.Wait(0))
		{
			return;
		}

		try
		{
			foreach (var instance in _instances)
			{
				instance.Player.Dispose();
				instance.Tokens.Dispose();
			}

			_instances = [];
			_scopesOutdated = false;
			_tokenPersister?.Dispose();
			_tokenPersister = null;

			IsInitialized = false;
		}
		finally
		{
			_lifecycleGate.Release();
		}
	}

	public async Task<IReadOnlyList<IntegrationIssue>> GetIssuesAsync(CancellationToken cancellationToken = default)
	{
		if (!IsInitialized || _context is null)
		{
			if (!_issuePollSilencedNotInitializedLogged)
			{
				_issuePollSilencedNotInitializedLogged = true;
				_logger.Information("Spotify issue poll: integration is not initialized; reporting no issues");
			}

			return [];
		}

		_issuePollSilencedNotInitializedLogged = false;

		var entries = await _context.Config.GetEntriesAsync(cancellationToken);
		if (entries.Count == 0)
		{
			if (!_issuePollSilencedNoEntriesLogged)
			{
				_issuePollSilencedNoEntriesLogged = true;
				_logger.Information("Spotify issue poll: no config entries; reporting no issues");
			}

			return [];
		}

		_issuePollSilencedNoEntriesLogged = false;

		// TemporarilyUnavailable deliberately raises nothing: Macro Deck retries on its own and keeps the
		// last known state, so a network blink must not send the user through OAuth again (#298).
		var instances = _instances;
		var credentialsInvalid = instances.Count == 0 ||
			instances.Any(i => i.Player.AuthenticationState == SpotifyAuthenticationState.ReauthorizationRequired);
		if (credentialsInvalid)
		{
			return SingleIssue(new IntegrationIssue
			{
				Id = CredentialsIssueId,
				Title = AppStrings.Integrations.Spotify.Issues.CredentialsInvalidTitle(),
				Description = AppStrings.Integrations.Spotify.Issues.CredentialsInvalidDescription(),
				Severity = IntegrationIssueSeverity.Warning,
				ActionLabel = AppStrings.Integrations.Spotify.Issues.ReconnectAction()
			});
		}

		if (_scopesOutdated)
		{
			return SingleIssue(new IntegrationIssue
			{
				Id = ScopesOutdatedIssueId,
				Title = AppStrings.Integrations.Spotify.Issues.ScopesOutdatedTitle(),
				Description = AppStrings.Integrations.Spotify.Issues.ScopesOutdatedDescription(),
				Severity = IntegrationIssueSeverity.Warning,
				ActionLabel = AppStrings.Integrations.Spotify.Issues.ReconnectAction()
			});
		}

		// Spotify's Web API only lets Premium accounts control playback, and since February 2026 the
		// profile no longer reports the subscription level - a refused command is the only thing that
		// says so. Ahead of the limit and unreachable issues: the account cannot work at all, while
		// those two describe conditions that pass.
		if (instances.Any(i => i.Player.PremiumRequired))
		{
			return SingleIssue(new IntegrationIssue
			{
				Id = PremiumRequiredIssueId,
				Title = AppStrings.Integrations.Spotify.Issues.PremiumRequiredTitle(),
				Description = AppStrings.Integrations.Spotify.Issues.PremiumRequiredDescription(
					url: SpotifyWebApiDocsUrl),
				Severity = IntegrationIssueSeverity.Warning,
				ActionLabel = AppStrings.Integrations.Spotify.Issues.ReconnectAction()
			});
		}

		if (ApiLimit(instances) is { } limit)
		{
			var quota = limit.Kind == SpotifyApiLimitKind.Quota;
			var format = await TimeOfDayFormatAsync();
			var pausedAt = LocalTimeOfDay(format, limit.Since);
			var resumesAt = limit.PausedUntil.ToLocalTime();
			var resumesTime = LocalTimeOfDay(format, limit.PausedUntil);
			var sameDay = resumesAt.Date == DateTimeOffset.Now.Date;
			return SingleIssue(new IntegrationIssue
			{
				Id = ApiLimitIssueId,
				Title = quota
					? AppStrings.Integrations.Spotify.Issues.QuotaReachedTitle()
					: AppStrings.Integrations.Spotify.Issues.RateLimitedTitle(),
				Description = quota
					? (sameDay
						? AppStrings.Integrations.Spotify.Issues.QuotaReachedDescriptionSameDay(pausedAt: pausedAt,
							resumesAt: resumesTime)
						: AppStrings.Integrations.Spotify.Issues.QuotaReachedDescriptionOtherDay(pausedAt: pausedAt,
							resumesDate: resumesAt.ToString("d MMMM", CultureInfo.CurrentCulture),
							resumesTime: resumesTime))
					: (sameDay
						? AppStrings.Integrations.Spotify.Issues.RateLimitedDescriptionSameDay(pausedAt: pausedAt,
							resumesAt: resumesTime)
						: AppStrings.Integrations.Spotify.Issues.RateLimitedDescriptionOtherDay(pausedAt: pausedAt,
							resumesDate: resumesAt.ToString("d MMMM", CultureInfo.CurrentCulture),
							resumesTime: resumesTime)),
				Severity = IntegrationIssueSeverity.Warning,
				ActionLabel = null
			});
		}

		if (UnreachableSince(instances) is { } since && DateTimeOffset.UtcNow - since >= _unreachableIssueThreshold)
		{
			return SingleIssue(new IntegrationIssue
			{
				Id = UnreachableIssueId,
				Title = AppStrings.Integrations.Spotify.Issues.NotRespondingTitle(),
				Description = AppStrings.Integrations.Spotify.Issues.NotRespondingDescription(
					since: LocalTimeOfDay(await TimeOfDayFormatAsync(), since)),
				Severity = IntegrationIssueSeverity.Info,
				ActionLabel = null
			});
		}

		return [];
	}

	private static List<IntegrationIssue> SingleIssue(IntegrationIssue primary)
		=> [primary];

	private Task<TimeOfDayFormat> TimeOfDayFormatAsync()
		=> (_timeOfDayFormat ?? HostTimeOfDayFormat)?.Invoke() ??
			Task.FromResult(new TimeOfDayFormat(CultureInfo.CurrentCulture, HourCycles.H23));

	private static string LocalTimeOfDay(TimeOfDayFormat format, DateTimeOffset instant)
		=> format.Format(TimeOnly.FromDateTime(instant.ToLocalTime().DateTime));

	// The longest-running episode across instances, so a second account joining a limit that is already
	// open cannot restart the issue's timestamp (and with it its JSON, and with it a re-send).
	private static SpotifyApiLimitStatus? ApiLimit(IReadOnlyList<Instance> instances)
	{
		SpotifyApiLimitStatus? earliest = null;
		foreach (var instance in instances)
		{
			if (instance.Player.ApiLimit is { } limit && (earliest is null || limit.Since < earliest.Since))
			{
				earliest = limit;
			}
		}

		return earliest;
	}

	private static DateTimeOffset? UnreachableSince(IReadOnlyList<Instance> instances)
	{
		DateTimeOffset? earliest = null;
		foreach (var instance in instances)
		{
			if (instance.Player.UnreachableSince is { } since && (earliest is null || since < earliest))
			{
				earliest = since;
			}
		}

		return earliest;
	}

	public Task<IssueResolution> ResolveIssueAsync(string issueId, CancellationToken cancellationToken = default)
		=> Task.FromResult(issueId switch
		{
			CredentialsIssueId =>
				IssueResolution.Ok(AppStrings.Integrations.Spotify.Issues.ReenterCredentialsResolution(),
					IssueResolutionFollowUp.StartConfigFlow),
			ScopesOutdatedIssueId =>
				IssueResolution.Ok(AppStrings.Integrations.Spotify.Issues.ReconnectForPermissionsResolution(),
					IssueResolutionFollowUp.StartConfigFlow),
			PremiumRequiredIssueId =>
				IssueResolution.Ok(AppStrings.Integrations.Spotify.Issues.ReconnectWithPremiumResolution(),
					IssueResolutionFollowUp.StartConfigFlow),
			_ => IssueResolution.Failed(AppStrings.Integrations.Issues.UnknownIssue())
		});

	public async ValueTask<VariableReading> ReadAsync(string localId, CancellationToken cancellationToken = default)
	{
		var player = DefaultPlayer;
		var state = player?.CurrentState ?? MusicPlayerState.Disconnected;

		var topItems = localId.StartsWith("spotify-top-", StringComparison.Ordinal) ? player?.GetTopItems() : null;

		return localId switch
		{
			"spotify-current-track-name" => VariableReading.Of(state.TrackName),
			"spotify-current-artist" =>
				VariableReading.Of(state.Artists.Count > 0 ? string.Join(", ", state.Artists) : null),
			"spotify-current-album" => VariableReading.Of(state.AlbumName),
			"spotify-playback-state" => VariableReading.Of(state.PlaybackState.ToString().ToLowerInvariant()),
			"spotify-is-playing" => VariableReading.Of(state.PlaybackState == PlaybackState.Playing),
			VolumeId => VariableReading.Of(state.VolumePercent, 0, 100, 1),
			"spotify-track-duration" => VariableReading.Of(state.Duration is { } d ? (int)d.TotalSeconds : null),
			// The seek range is the current track's length, which is exactly why bounds ride on the
			// reading rather than on the declaration.
			CurrentPositionId => VariableReading.Of(state.Position is { } p ? (int)p.TotalSeconds : null,
				0,
				state.Duration is { } length ? (int)length.TotalSeconds : null,
				1),
			"spotify-progress-percentage" => VariableReading.Of(Progress(state)),
			"spotify-album-art-url" => VariableReading.Of(ArtworkUrl(state.ArtworkId)),
			"spotify-device-name" => VariableReading.Of(state.DeviceName),
			"spotify-device-type" => VariableReading.Of(state.DeviceType),
			"spotify-shuffle-enabled" => VariableReading.Of(state.ShuffleEnabled),
			"spotify-repeat-mode" => VariableReading.Of(state.RepeatMode.ToString().ToLowerInvariant()),
			"spotify-is-connected" => VariableReading.Of(state.IsConnected),
			"spotify-current-track-url" => VariableReading.Of(player?.CurrentItem?.Url),
			"spotify-current-track-is-liked" => player is null
				? VariableReading.Unavailable
				: VariableReading.Of(await player.IsCurrentItemSavedAsync(cancellationToken)),
			"spotify-top-track" => VariableReading.Of(topItems?.TopTrack),
			"spotify-top-artist" => VariableReading.Of(topItems?.TopArtist),
			"spotify-top-tracks" => VariableReading.Of(topItems is { TopTracks.Count: > 0 }
				? string.Join(", ", topItems.TopTracks)
				: null),
			"spotify-top-artists" => VariableReading.Of(topItems is { TopArtists.Count: > 0 }
				? string.Join(", ", topItems.TopArtists)
				: null),
			_ => VariableReading.Unavailable
		};
	}

	public ValueTask<VariableWriteResult> SetValueAsync(
		string localId,
		object? value,
		CancellationToken cancellationToken = default)
	{
		// Passing null rather than a disconnected player is what turns "Spotify is not playing anywhere"
		// into a retryable Unavailable instead of an outright failure.
		var player = DefaultPlayer is { CurrentState.IsConnected: true } connected ? connected : null;

		return localId switch
		{
			VolumeId => MusicPlayerVariableWrites.SetVolumeAsync(player, value, cancellationToken),
			CurrentPositionId => MusicPlayerVariableWrites.SeekAsync(player, value, cancellationToken),
			_ => ValueTask.FromResult(VariableWriteResult.NotWritable())
		};
	}

	private string? ArtworkUrl(string? artworkId)
	{
		var instances = _instances;
		var instanceId = instances.Count > 0 ? instances[0].EntryId : null;
		if (artworkId is null || instanceId is null)
		{
			return null;
		}

		return
			$"{ArtworkBaseUrl}{Uri.EscapeDataString(artworkId)}?instanceId={Uri.EscapeDataString($"{Id}::{instanceId}")}";
	}

	private static int? Progress(MusicPlayerState state)
	{
		if (state.Duration is not { } duration || duration.TotalMilliseconds <= 0 || state.Position is not { } position)
		{
			return null;
		}

		return (int)Math.Clamp(position.TotalMilliseconds / duration.TotalMilliseconds * 100, 0, 100);
	}

	private async Task ConnectFromConfig(IIntegrationContext context, SpotifyTokenPersister tokenPersister)
	{
		var entries = await context.Config.GetEntriesAsync();
		var instances = new List<Instance>();
		var scopesOutdated = false;

		try
		{
			foreach (var entry in entries)
			{
				var built = await BuildPlayer(context, tokenPersister, entry.Id);
				if (built is not null)
				{
					instances.Add(new Instance(entry.Id.ToString(), entry.Title, built.Player, built.Tokens));
					scopesOutdated |= built.ScopesOutdated;
				}
			}
		}
		catch
		{
			await StopInstancesAsync(instances);
			throw;
		}

		_instances = instances;
		_scopesOutdated = scopesOutdated;
		_logger.Information("Spotify connected with {Count} instance(s)", instances.Count);
	}

	private static async Task StopInstancesAsync(IReadOnlyList<Instance> instances)
	{
		await Task.WhenAll(instances.Select(instance => instance.Player.DisconnectAsync()));

		// Drain a refresh that already received a rotated token so its write lands before the exact
		// persistence generation it targets is closed. StopAsync is internally bounded, so this cannot hang.
		try
		{
			await Task.WhenAll(instances.Select(instance => instance.Tokens.StopAsync(_tokenDrainTimeout)));
		}
		finally
		{
			foreach (var instance in instances)
			{
				instance.Tokens.Dispose();
				instance.Player.Dispose();
			}
		}
	}

	private async Task<BuiltPlayer?> BuildPlayer(
		IIntegrationContext context,
		SpotifyTokenPersister tokenPersister,
		Guid entryId)
	{
		var clientId = await context.Config.GetStringAsync(entryId, SpotifyConfigKeys.ClientId);
		var clientSecret = await context.Config.GetSecretAsync(entryId, SpotifyConfigKeys.ClientSecret);
		var accessToken = await context.Config.GetSecretAsync(entryId, SpotifyConfigKeys.AccessToken);
		var refreshToken = await context.Config.GetSecretAsync(entryId, SpotifyConfigKeys.RefreshToken);
		var expiresAtRaw = await context.Config.GetStringAsync(entryId, SpotifyConfigKeys.ExpiresAt);
		var scope = await context.Config.GetStringAsync(entryId, SpotifyConfigKeys.Scope);

		if (string.IsNullOrEmpty(clientId) ||
			string.IsNullOrEmpty(clientSecret) ||
			string.IsNullOrEmpty(accessToken) ||
			string.IsNullOrEmpty(refreshToken))
		{
			_logger.Warning("Spotify config entry {EntryId} is incomplete; skipping", entryId);
			return null;
		}

		var expiresAt = DateTimeOffset.TryParse(expiresAtRaw,
			CultureInfo.InvariantCulture,
			DateTimeStyles.RoundtripKind,
			out var parsed)
			? parsed.UtcDateTime
			: DateTime.UtcNow;

		var tokens = new SpotifyTokenManager(entryId,
			clientId,
			clientSecret,
			SpotifyTokenSnapshot.FromStored(accessToken,
				refreshToken,
				expiresAt,
				SpotifyTokenManager.RefreshMargin),
			_oauth,
			tokenPersister,
			_logger,
			persistenceBudget: _persistenceBudget,
			timeProvider: _timeProvider);

		// One limiter per entry, because Spotify counts per app (client id) and an entry is one app. It
		// wraps the transport rather than sitting in the player, so the retry handler's own attempts and
		// every path added later have to pay for their permits too.
		var limiter = new SpotifyRequestLimiter();

		var config = SpotifyClientConfig.CreateDefault()
			.WithAuthenticator(new SpotifyAccessTokenAuthenticator(tokens))
			.WithHTTPClient(new SpotifyThrottledHttpClient(_transport, limiter))
			.WithRetryHandler(new SpotifyRetryHandler());

		var limitStore = new SpotifyApiLimitStore(context.Config, _logger);
		var player = new SpotifyMusicPlayer(limitStore: limitStore,
			timeProvider: _timeProvider,
			autonomousPolling: true);
		player.Connect(config, tokens, await limitStore.LoadAsync(entryId));

		var scopesOutdated = string.IsNullOrEmpty(scope) || SpotifyScopes.Missing(scope).Count > 0;
		return new BuiltPlayer(player, tokens, scopesOutdated);
	}

	private static byte[] LoadIcon()
	{
		var assembly = typeof(SpotifyIntegration).Assembly;
		var name = assembly.GetManifestResourceNames()
			.First(n => n.EndsWith("spotify-icon.svg", StringComparison.Ordinal));
		using var stream = assembly.GetManifestResourceStream(name)!;
		using var memory = new MemoryStream();
		stream.CopyTo(memory);
		return memory.ToArray();
	}

	private sealed record Instance(
		string EntryId,
		string Title,
		SpotifyMusicPlayer Player,
		SpotifyTokenManager Tokens);

	private sealed record BuiltPlayer(SpotifyMusicPlayer Player, SpotifyTokenManager Tokens, bool ScopesOutdated);
}
