using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using MacroDeck.Sdk.Logging;
using MacroDeck.Sdk.MusicPlayer;
using Serilog;
using SpotifyAPI.Web;
using SpotifyAPI.Web.Http;

namespace MacroDeckHost.Integrations.Spotify;

internal sealed class SpotifyMusicPlayer : ICatalogMusicPlayer, IMusicPlayerDeviceProvider, IDisposable
{
	private static readonly HttpClient _http = SpotifyHttpClients.CreatePooled(TimeSpan.FromSeconds(10));

	// Liking a song in the phone app must not leave the deck button showing the old state for the rest of
	// the track, so the per-URI answer expires even though nothing local changed. One CheckItems call.
	private static readonly TimeSpan _savedTtl = TimeSpan.FromSeconds(45);

	private static readonly TimeSpan _savedFailureBackoff = TimeSpan.FromSeconds(45);

	// Playlist membership is cached per playlist rather than per (playlist, track): one page walk answers
	// every button and every track, so N buttons following the same playlist cost one fetch per TTL. A
	// playlist longer than the cap is not walked at all - an unbounded page walk on a poll path would be
	// exactly the per-button API traffic this must avoid - and reports "unknown" instead.
	private static readonly TimeSpan _playlistMembershipTtl = TimeSpan.FromSeconds(60);

	private static readonly TimeSpan _playlistMembershipFailureBackoff = TimeSpan.FromSeconds(60);

	private const int PlaylistMembershipPageSize = 100;

	private const int PlaylistMembershipMaxItems = 1000;

	// 30 min TTL, 5 min failure backoff so a stale/unreachable snapshot recovers on its own, and 20s
	// fetch timeout so a slow refresh cannot pin a device connection indefinitely.
	private static readonly TimeSpan _topItemsTtl = TimeSpan.FromMinutes(30);
	private static readonly TimeSpan _topItemsFailureBackoff = TimeSpan.FromMinutes(5);
	private static readonly TimeSpan _topItemsFetchTimeout = TimeSpan.FromSeconds(20);

	private readonly ConcurrentDictionary<string, string> _artworkUrls = new(StringComparer.Ordinal);

	private static readonly TimeSpan _defaultLastStateLifetime = TimeSpan.FromMinutes(2);

	private static readonly TimeSpan _defaultRateLimitPause = TimeSpan.FromSeconds(10);

	private static readonly TimeSpan _quotaPause = TimeSpan.FromMinutes(5);

	// The host's poll boundary cancels at 10s, and a cancellation cannot be classified - which is
	// exactly how a rate limit used to become invisible: SimpleRetryHandler slept out the Retry-After,
	// the boundary fired first, and all the poll ever produced was "cancelled during playback after
	// 10000 ms". Timing out inside the player keeps every failure attributable to a cause.
	private static readonly TimeSpan _defaultStateReadBudget = TimeSpan.FromSeconds(8);

	private static readonly TimeSpan _idleDevicesTtl = TimeSpan.FromSeconds(15);

	private static readonly TimeSpan _idleFailureBackoff = TimeSpan.FromSeconds(15);

	private readonly ILogger _logger;

	private readonly FailureEpisodeTracker _stateReadFailures;

	private readonly FailureEpisodeTracker _apiLimits;

	private readonly TimeSpan _lastStateLifetime;
	private readonly TimeSpan _stateReadBudget;
	private readonly TimeSpan _rateLimitPause;

	private readonly SpotifyApiLimitStore? _limitStore;

	private readonly TimeProvider _time;
	private readonly bool _autonomousPolling;
	private readonly SemaphoreSlim _pollGate = new(1, 1);
	private readonly Lock _pollControl = new();
	private readonly Lock _lifecycle = new();

	private volatile SpotifyClient? _client;

	// The same connector the client above runs on, kept because two of Spotify's endpoints cannot be
	// reached through SpotifyAPI-NET's typed request models - see ToggleLikedAsync.
	private volatile IAPIConnector? _connector;
	private volatile SpotifyTokenManager? _tokens;
	private volatile bool _accessTokenRejected;
	private volatile SpotifyPlayingItem? _currentItem;
	private volatile SpotifyApiLimitStatus? _apiLimit;
	private volatile bool _premiumRequired;

	private Guid? _entryId;
	private bool _noClientLogged;
	private DateTime _lastSuccessfulReadUtc;
	private long _rateLimitPauseUntilTicks;
	private MusicPlayerState? _idleState;
	private DateTime _idleStateAtUtc;
	private DateTime _idleRetryNotBeforeUtc;
	private long _stateReadAtTicks;
	private CancellationTokenSource? _pollCts;
	private CancellationTokenSource? _pollDelayWake;
	private Task? _pollTask;
	private Task? _disconnectTask;
	private TaskCompletionSource<MusicPlayerState>? _manualRefreshWaiter;
	private bool _immediatePollPending;
	private int _actionFollowUpsRemaining;
	private int _transientPollFailure;

	public SpotifyMusicPlayer(ILogger? logger = null,
		TimeSpan? failureSummaryInterval = null,
		TimeSpan? lastStateLifetime = null,
		TimeSpan? stateReadBudget = null,
		TimeSpan? rateLimitPause = null,
		SpotifyApiLimitStore? limitStore = null,
		TimeProvider? timeProvider = null,
		bool autonomousPolling = false)
	{
		_logger = logger ?? IntegrationLog.For<SpotifyMusicPlayer>(SpotifyIntegration.IntegrationId);
		_stateReadFailures = new FailureEpisodeTracker(failureSummaryInterval);
		_apiLimits = new FailureEpisodeTracker(failureSummaryInterval);
		_lastStateLifetime = lastStateLifetime ?? _defaultLastStateLifetime;
		_stateReadBudget = stateReadBudget ?? _defaultStateReadBudget;
		_rateLimitPause = rateLimitPause ?? _defaultRateLimitPause;
		_limitStore = limitStore;
		_time = timeProvider ?? TimeProvider.System;
		_autonomousPolling = autonomousPolling;
	}

	private readonly Lock _savedLock = new();
	private string? _savedUri;
	private bool? _savedValue;
	private DateTime _savedAtUtc;
	private DateTime _savedRetryNotBeforeUtc;
	private volatile bool _savedLatchedOff;

	private readonly Lock _playlistMembershipLock = new();

	private readonly Dictionary<string, PlaylistMembership> _playlistMembership = new(StringComparer.Ordinal);

	private DateTime _playlistMembershipRetryNotBeforeUtc;

	private volatile SpotifyTopItems? _topItems;
	private volatile bool _topItemsLatchedOff;
	private long _topItemsNextFetchTicks;
	private int _topItemsFetching;

	public MusicPlayerState LastState { get; private set; } = MusicPlayerState.Disconnected;

	// A 5s poll cadence would otherwise freeze spotify_current_position and spotify_progress_percentage
	// for up to 5s and then jump - they are read from cached state without triggering a poll, unlike the
	// music-player widget, which interpolates client-side on its own. Paused states carry a Position too,
	// but never advance here: only a Playing state's elapsed wall-clock time is added.
	public MusicPlayerState CurrentState
	{
		get
		{
			var state = LastState;
			if (state is not { IsConnected: true, PlaybackState: PlaybackState.Playing, Position: { } position })
			{
				return state;
			}

			var advanced = position +
				(_time.GetUtcNow() - new DateTimeOffset(Interlocked.Read(ref _stateReadAtTicks), TimeSpan.Zero));
			var bound = state.Duration ?? TimeSpan.MaxValue;
			return state with { Position = advanced < bound ? advanced : bound };
		}
	}

	public SpotifyPlayingItem? CurrentItem => _currentItem;

	public SpotifyAuthenticationState AuthenticationState
		=> _accessTokenRejected
			? SpotifyAuthenticationState.ReauthorizationRequired
			: _tokens?.State ?? SpotifyAuthenticationState.Valid;

	public SpotifyApiLimitStatus? ApiLimit => _apiLimit;

	public bool PremiumRequired => _premiumRequired;

	private bool ReadsPaused => _time.GetUtcNow().UtcTicks < Interlocked.Read(ref _rateLimitPauseUntilTicks);

	public DateTimeOffset? UnreachableSince
	{
		get
		{
			var stateReads = _stateReadFailures.StartedAt;
			var refreshes = _tokens?.UnavailableSince;
			if (stateReads is null || refreshes is null)
			{
				return stateReads ?? refreshes;
			}

			return stateReads < refreshes ? stateReads : refreshes;
		}
	}

	public void Connect(SpotifyClientConfig config, SpotifyTokenManager tokens, SpotifyApiLimitStatus? openLimit = null)
	{
		lock (_lifecycle)
		{
			if (_disconnectTask is not null ||
				(_autonomousPolling && (_client is not null || _pollCts is not null || _pollTask is not null)))
			{
				throw new InvalidOperationException("The Spotify player is already connected or disconnecting");
			}

			var connector = new APIConnector(config.BaseAddress,
				config.Authenticator!,
				config.JSONSerializer,
				config.HTTPClient,
				config.RetryHandler,
				config.HTTPLogger);

			_connector = connector;
			_client = new SpotifyClient(config.WithAPIConnector(connector));
			_tokens = tokens;
			_entryId = tokens.EntryId;
			_accessTokenRejected = false;
			_currentItem = null;
			_noClientLogged = false;
			_premiumRequired = false;
			LastState = MusicPlayerState.Disconnected;
			_lastSuccessfulReadUtc = _time.GetUtcNow().UtcDateTime;
			_idleState = null;
			lock (_pollControl)
			{
				_immediatePollPending = false;
				_actionFollowUpsRemaining = 0;
				_manualRefreshWaiter = null;
			}

			Interlocked.Exchange(ref _transientPollFailure, 0);
			// Spotify keys a rate-limit penalty to the app's client id. Preserve a carried-over pause
			// rather than immediately resuming the request storm that caused it.
			Resume(openLimit);
			if (_autonomousPolling)
			{
				var pollCts = new CancellationTokenSource();
				_pollCts = pollCts;
				_pollTask = PollLoopAsync(pollCts.Token);
			}
		}
	}

	private void Resume(SpotifyApiLimitStatus? openLimit)
	{
		if (openLimit is not { } limit || limit.PausedUntil <= _time.GetUtcNow())
		{
			return;
		}

		_apiLimit = limit;
		Interlocked.Exchange(ref _rateLimitPauseUntilTicks, limit.PausedUntil.UtcDateTime.Ticks);
		_apiLimits.RecordFailure($"{limit.Kind} carried over from a previous run");
		_logger.Warning("Spotify is still limiting entry {EntryId} from a previous run; reads stay paused until " +
			"{ResumesAt:yyyy-MM-dd HH:mm:ss}",
			_entryId,
			limit.PausedUntil.ToLocalTime());
	}

	public Task DisconnectAsync()
	{
		lock (_lifecycle)
		{
			if (_disconnectTask is not null)
			{
				return _disconnectTask;
			}

			if (_client is null && _pollCts is null && _pollTask is null)
			{
				return Task.CompletedTask;
			}

			var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
			_disconnectTask = completion.Task;
			_pollCts?.Cancel();
			WakePollDelay();
			_ = CompleteDisconnectAsync(_pollCts, _pollTask, completion);
			return completion.Task;
		}
	}

	private async Task CompleteDisconnectAsync(CancellationTokenSource? pollCts,
		Task? pollTask,
		TaskCompletionSource completion)
	{
		Exception? failure = null;
		try
		{
			if (pollTask is not null)
			{
				try
				{
					await pollTask;
				}
				catch (OperationCanceledException)
				{
				}
			}
		}
		catch (Exception ex)
		{
			failure = ex;
		}
		finally
		{
			lock (_lifecycle)
			{
				if (LastState.IsConnected)
				{
					_logger.Information(
						"Spotify player for entry {EntryId} disconnected (integration shutdown or reconfigure)",
						_entryId);
				}

				_pollCts = null;
				_pollTask = null;
				_client = null;
				_connector = null;
				_tokens = null;
				LastState = MusicPlayerState.Disconnected;
				_currentItem = null;
				_disconnectTask = null;
			}

			pollCts?.Dispose();
		}

		if (failure is null)
		{
			completion.TrySetResult();
		}
		else
		{
			completion.TrySetException(failure);
		}
	}

	public void Dispose()
	{
		Disconnect();
	}

	public void Disconnect() => DisconnectAsync().GetAwaiter().GetResult();

	private async Task<SpotifyClient?> PrepareAsync(CancellationToken cancellationToken)
	{
		var client = _client;
		var tokens = _tokens;
		if (client is null || tokens is null)
		{
			return null;
		}

		await tokens.EnsureValidTokenAsync(cancellationToken);
		return client;
	}

	public Task<MusicPlayerState> GetStateAsync(CancellationToken cancellationToken = default)
		=> _autonomousPolling ? Task.FromResult(CurrentState) : ExecutePollAsync(cancellationToken);

	internal Task<MusicPlayerState> RefreshStateAsync(CancellationToken cancellationToken = default)
	{
		if (!_autonomousPolling)
		{
			return ExecutePollAsync(cancellationToken);
		}

		cancellationToken.ThrowIfCancellationRequested();
		TaskCompletionSource<MusicPlayerState> waiter;
		lock (_pollControl)
		{
			waiter = _manualRefreshWaiter ??=
				new TaskCompletionSource<MusicPlayerState>(TaskCreationOptions.RunContinuationsAsynchronously);
			_immediatePollPending = true;
			_pollDelayWake?.Cancel();
		}

		return waiter.Task.WaitAsync(cancellationToken);
	}

	private async Task PollLoopAsync(CancellationToken cancellationToken)
	{
		try
		{
			var delay = TimeSpan.Zero;
			while (!cancellationToken.IsCancellationRequested)
			{
				if (delay > TimeSpan.Zero)
				{
					await DelayOrWakeAsync(delay, cancellationToken);
				}

				cancellationToken.ThrowIfCancellationRequested();
				TaskCompletionSource<MusicPlayerState>? manualRefresh;
				lock (_pollControl)
				{
					_immediatePollPending = false;
					manualRefresh = _manualRefreshWaiter;
					_manualRefreshWaiter = null;
				}

				try
				{
					var state = await ExecutePollAsync(cancellationToken);
					manualRefresh?.TrySetResult(state);
				}
				catch (Exception ex)
				{
					manualRefresh?.TrySetException(ex);
					throw;
				}

				lock (_pollControl)
				{
					if (_immediatePollPending)
					{
						_immediatePollPending = false;
						delay = TimeSpan.Zero;
					}
					else if (_actionFollowUpsRemaining > 0)
					{
						_actionFollowUpsRemaining--;
						delay = SpotifyPollSchedule.ActionFollowUpInterval;
					}
					else if (ReadsPaused)
					{
						delay = TimeSpan.FromTicks(Math.Max(0,
							Interlocked.Read(ref _rateLimitPauseUntilTicks) - _time.GetUtcNow().UtcTicks));
					}
					else if (Interlocked.Exchange(ref _transientPollFailure, 0) != 0)
					{
						delay = SpotifyPollSchedule.TransientFailureInterval;
					}
					else
					{
						delay = SpotifyPollSchedule.AfterState(LastState);
					}
				}
			}
		}
		finally
		{
			TaskCompletionSource<MusicPlayerState>? manualRefresh;
			lock (_pollControl)
			{
				manualRefresh = _manualRefreshWaiter;
				_manualRefreshWaiter = null;
			}

			manualRefresh?.TrySetCanceled(cancellationToken);
		}
	}

	private async Task DelayOrWakeAsync(TimeSpan delay, CancellationToken cancellationToken)
	{
		using var wake = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		lock (_pollControl)
		{
			_pollDelayWake = wake;
			if (_immediatePollPending)
			{
				wake.Cancel();
			}
		}

		try
		{
			await Task.Delay(delay, _time, wake.Token);
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
		}
		finally
		{
			lock (_pollControl)
			{
				if (ReferenceEquals(_pollDelayWake, wake))
				{
					_pollDelayWake = null;
				}
			}
		}
	}

	private void RequestActionPollBurst()
	{
		lock (_pollControl)
		{
			_immediatePollPending = true;
			_actionFollowUpsRemaining = 3;
			_pollDelayWake?.Cancel();
		}
	}

	private void WakePollDelay()
	{
		lock (_pollControl)
		{
			_pollDelayWake?.Cancel();
		}
	}

	private async Task<MusicPlayerState> ExecutePollAsync(CancellationToken cancellationToken)
	{
		await _pollGate.WaitAsync(cancellationToken);
		try
		{
			return await PollOnceAsync(cancellationToken);
		}
		finally
		{
			_pollGate.Release();
		}
	}

	private async Task<MusicPlayerState> PollOnceAsync(CancellationToken cancellationToken)
	{
		Interlocked.Exchange(ref _transientPollFailure, 0);
		if (ReadsPaused)
		{
			// Honouring Retry-After: no API call and no per-tick log during the pause. The freshness rule
			// still applies - a long pause must not freeze "Playing X" for its whole duration.
			return KeepLastState();
		}

		using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		budget.CancelAfter(_stateReadBudget);
		var readToken = budget.Token;

		// Droppable: a poll the local limiter cannot afford costs one stale tick, while sending it anyway
		// is what earns the 429 that costs hours.
		using var scope = SpotifyRequestScope.Poll();

		for (var attempt = 0;; attempt++)
		{
			var stopwatch = Stopwatch.StartNew();

			var phase = "authentication";
			string? usedAccessToken = null;
			try
			{
				var client = _client;
				var tokens = _tokens;
				if (client is null || tokens is null)
				{
					if (!_noClientLogged)
					{
						_noClientLogged = true;
						_logger.Information(
							"Spotify state poll found no connected client; reporting disconnected until the " +
							"integration (re)initializes");
					}

					return Publish(MusicPlayerState.Disconnected, null, "no client connected");
				}

				await tokens.EnsureValidTokenAsync(readToken);
				usedAccessToken = tokens.AccessToken;

				var authElapsed = stopwatch.Elapsed;
				phase = "playback";
				var playback = await client.Player.GetCurrentPlayback(readToken);
				var state = playback is not null
					? Map(playback)
					: await BuildIdleStateAsync(client, readToken);
				tokens.NoteAccessTokenAccepted();
				ClearAuthFailure();
				NoteStateReadRecovered();
				NoteApiLimitRecovered();
				_lastSuccessfulReadUtc = _time.GetUtcNow().UtcDateTime;

				// Only when it was actually slow, so the 2s poll cadence cannot spam this.
				if (stopwatch.Elapsed > TimeSpan.FromSeconds(2))
				{
					_logger.Debug("Spotify state read took {ElapsedMs} ms, {AuthMs} ms of it waiting for a token",
						stopwatch.ElapsedMilliseconds,
						(long)authElapsed.TotalMilliseconds);
				}

				return Publish(state, SpotifyPlayingItem.From(playback?.Item), "state read succeeded");
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				_logger.Debug("Spotify state read was cancelled during {Phase} after {ElapsedMs} ms",
					phase,
					stopwatch.ElapsedMilliseconds);
				throw;
			}
			catch (OperationCanceledException ex)
			{
				Interlocked.Exchange(ref _transientPollFailure, 1);
				if (NoteStateReadFailure(ex, $"no answer within {_stateReadBudget} during {phase}"))
				{
					_logger.Debug("Spotify state read gave up after {Budget} during {Phase}; keeping last state",
						_stateReadBudget,
						phase);
				}

				return KeepLastState();
			}
			catch (SpotifyAuthRejectedException ex)
			{
				RegisterAuthFailure(ex);
				return Publish(MusicPlayerState.Disconnected, null, "refresh token rejected");
			}
			catch (SpotifyAuthTransientException ex)
			{
				Interlocked.Exchange(ref _transientPollFailure, 1);
				if (ex.IsRateLimit)
				{
					PauseReads(SpotifyApiLimitKind.RateLimit,
						ex.RetryAfter is { } retryAfter && retryAfter > TimeSpan.Zero ? retryAfter : _rateLimitPause,
						ex);
				}

				if (NoteStateReadFailure(ex))
				{
					_logger.Debug("Spotify token could not be refreshed; keeping last state ({Failure})",
						SafeFailure(ex));
				}

				return KeepLastState();
			}
			catch (APIUnauthorizedException ex)
			{
				if (attempt == 0 && usedAccessToken is not null && _tokens is { } rejectedBy)
				{
					try
					{
						if (await rejectedBy.TryRefreshRejectedAccessTokenAsync(usedAccessToken, readToken))
						{
							continue;
						}
					}
					catch (SpotifyAuthRejectedException rejected)
					{
						RegisterAuthFailure(rejected);
						return Publish(MusicPlayerState.Disconnected, null, "refresh token rejected");
					}
					catch (SpotifyAuthTransientException transient)
					{
						Interlocked.Exchange(ref _transientPollFailure, 1);
						// The recovery refresh could not reach the token endpoint - that is an outage, not a
						// dead grant, and must not send the user through OAuth (#298).
						if (NoteStateReadFailure(transient))
						{
							_logger.Debug("Spotify token could not be refreshed; keeping last state ({Failure})",
								SafeFailure(transient));
						}

						return KeepLastState();
					}
				}

				RegisterAuthFailure(ex);
				return Publish(MusicPlayerState.Disconnected, null, "access token rejected (401)");
			}
			catch (Exception ex) when (IsInvalidGrant(ex))
			{
				RegisterAuthFailure(ex);
				return Publish(MusicPlayerState.Disconnected, null, "refresh token rejected (invalid_grant)");
			}
			catch (Exception ex) when (SpotifyApiLimits.IsRateLimited(ex))
			{
				Interlocked.Exchange(ref _transientPollFailure, 1);
				PauseReads(SpotifyApiLimitKind.RateLimit, SpotifyApiLimits.PauseFor(ex, _rateLimitPause), ex);
				NoteStateReadFailure(ex);
				return KeepLastState();
			}
			catch (Exception ex) when (SpotifyApiLimits.IsQuotaExceeded(ex))
			{
				Interlocked.Exchange(ref _transientPollFailure, 1);
				PauseReads(SpotifyApiLimitKind.Quota, _quotaPause, ex);
				NoteStateReadFailure(ex, "Spotify app quota exhausted");
				return KeepLastState();
			}
			catch (Exception ex) when (IsTransient(ex))
			{
				Interlocked.Exchange(ref _transientPollFailure, 1);
				if (NoteStateReadFailure(ex))
				{
					_logger.Debug("Transient failure reading Spotify playback state; keeping last state ({Failure})",
						SafeFailure(ex));
				}

				return KeepLastState();
			}
			catch (Exception ex)
			{
				_logger.Error("Failed to read Spotify playback state ({Failure})", SafeFailure(ex));
				return Publish(MusicPlayerState.Disconnected, null, ex.GetType().Name);
			}
		}
	}

	private bool NoteStateReadFailure(Exception ex, string? error = null)
	{
		var signal = _stateReadFailures.RecordFailure(error ?? SafeFailure(ex));
		switch (signal.Kind)
		{
			case FailureEpisodeSignalKind.Onset:
				_logger.Warning(
					"Spotify state reads for entry {EntryId} started failing; keeping last state ({Failure})",
					_entryId,
					SafeFailure(ex));
				return false;
			case FailureEpisodeSignalKind.SummaryDue:
				_logger.Information(
					"Spotify state reads for entry {EntryId} have been failing for {Duration} ({Failures} " +
					"consecutive failures; last error: {LastError})",
					_entryId,
					signal.Duration,
					signal.ConsecutiveFailures,
					signal.LastError);
				return false;
			default:
				return true;
		}
	}

	private void NoteStateReadRecovered()
	{
		if (_stateReadFailures.RecordSuccess() is { } episode)
		{
			_logger.Information(
				"Spotify state reads for entry {EntryId} recovered after {Duration} ({Failures} failures)",
				_entryId,
				episode.Duration,
				episode.Failures);
		}
	}

	private void PauseReads(SpotifyApiLimitKind kind, TimeSpan pause, Exception ex)
	{
		var resumesAt = _time.GetUtcNow().Add(pause);
		Interlocked.Exchange(ref _rateLimitPauseUntilTicks, resumesAt.UtcTicks);

		var signal = _apiLimits.RecordFailure(SafeFailure(ex));
		var status = new SpotifyApiLimitStatus(kind, signal.StartedAt, resumesAt);
		_apiLimit = status;

		// Durable, because Spotify's Retry-After outlives this process more often than not.
		if (_entryId is { } entryId)
		{
			_limitStore?.Save(entryId, status);
		}

		var what = kind == SpotifyApiLimitKind.Quota
			? "refused the request because this Spotify app's quota is exhausted (HTTP 403)"
			: "rate-limited this Spotify app (HTTP 429)";

		switch (signal.Kind)
		{
			case FailureEpisodeSignalKind.Onset:
				_logger.Warning(
					"Spotify {What} for entry {EntryId}; pausing playback reads for {Pause}. The limit counts " +
					"every application using these Spotify app credentials, so temporarily closing other apps " +
					"on this account usually clears it",
					what,
					_entryId,
					pause);
				break;
			case FailureEpisodeSignalKind.SummaryDue:
				_logger.Information(
					"Spotify has been limiting entry {EntryId} for {Duration} ({Hits} refused requests; last: " +
					"{LastError}); reads paused until {ResumesAt:HH:mm:ss}",
					_entryId,
					signal.Duration,
					signal.ConsecutiveFailures,
					signal.LastError,
					resumesAt.ToLocalTime());
				break;
			default:
				_logger.Debug("Spotify still limiting entry {EntryId}; reads paused for another {Pause}",
					_entryId,
					pause);
				break;
		}
	}

	private void NoteApiLimitRecovered()
	{
		if (_apiLimit is not null && _entryId is { } entryId)
		{
			_limitStore?.Clear(entryId);
		}

		_apiLimit = null;
		if (_apiLimits.RecordSuccess() is { } episode)
		{
			_logger.Information("Spotify stopped limiting entry {EntryId} after {Duration} ({Hits} refused requests)",
				_entryId,
				episode.Duration,
				episode.Failures);
		}
	}

	private MusicPlayerState KeepLastState()
	{
		if (_time.GetUtcNow().UtcDateTime - _lastSuccessfulReadUtc < _lastStateLifetime)
		{
			return CurrentState;
		}

		if (LastState.IsConnected)
		{
			_logger.Warning("No successful Spotify state read for {Window}; reporting the player unavailable",
				_lastStateLifetime);
		}

		return Publish(Unavailable(), null, "stale beyond the freshness window");
	}

	// Unavailable, not Disconnected: the account is set up and Macro Deck is still retrying, so the
	// widget has to say "we cannot reach Spotify right now" rather than "no music player is configured",
	// which is what "Not connected" reads as.
	private MusicPlayerState Unavailable()
		=> MusicPlayerState.Unavailable(_apiLimit?.Kind switch
		{
			SpotifyApiLimitKind.Quota => "Spotify quota reached",
			SpotifyApiLimitKind.RateLimit => "Rate limited by Spotify",
			_ => "Spotify is not responding"
		});

	private MusicPlayerState Publish(MusicPlayerState state, SpotifyPlayingItem? item, string reason)
	{
		Interlocked.Exchange(ref _stateReadAtTicks, _time.GetUtcNow().UtcTicks);

		if (Describe(LastState) != Describe(state))
		{
			_logger.Information("Spotify entry {EntryId}: {Previous} -> {Current} ({Reason})",
				_entryId,
				Describe(LastState),
				Describe(state),
				reason);
		}

		_currentItem = item;
		return LastState = state;
	}

	private static string Describe(MusicPlayerState state) => state switch
	{
		{ IsConnected: true } => "connected",
		{ IsUnavailable: true } => "unavailable",
		_ => "disconnected"
	};

	private static string SafeFailure(Exception ex)
		=> ex is APIException { Response: { } response }
			? $"http-{(int)response.StatusCode}"
			: ex.GetType().Name;

	internal static bool IsTransient(Exception ex)
	{
		if (ex is APITooManyRequestsException or SpotifyThrottledException)
		{
			return true;
		}

		if (ex is APIException { Response: { } response })
		{
			return MusicPlayerTransientFailure.IsTransientStatusCode((int)response.StatusCode);
		}

		return MusicPlayerTransientFailure.IsNetworkLevel(ex);
	}

	internal static bool IsInvalidGrant(Exception ex)
	{
		for (var current = ex; current is not null; current = current.InnerException)
		{
			if (current is APIException apiException && MentionsInvalidGrant(apiException))
			{
				return true;
			}
		}

		return false;
	}

	private static bool MentionsInvalidGrant(APIException ex)
		=> (ex.Response?.Body is string body &&
				body.Contains("invalid_grant", StringComparison.OrdinalIgnoreCase)) ||
			ex.Message.Contains("invalid_grant", StringComparison.OrdinalIgnoreCase);

	public async Task<MusicPlayerArtwork?> GetArtworkAsync(
		string artworkId,
		CancellationToken cancellationToken = default)
	{
		if (!_artworkUrls.TryGetValue(artworkId, out var url))
		{
			return null;
		}

		try
		{
			var bytes = await _http.GetByteArrayAsync(url, cancellationToken);
			return new MusicPlayerArtwork(bytes, "image/jpeg");
		}
		catch (Exception ex)
		{
			_logger.Warning(ex, "Failed to download Spotify artwork");
			return null;
		}
	}

	public Task PlayAsync(CancellationToken cancellationToken = default)
		=> Guard(client => client.Player.ResumePlayback(cancellationToken), cancellationToken);

	public Task PauseAsync(CancellationToken cancellationToken = default)
		=> Guard(client => client.Player.PausePlayback(cancellationToken), cancellationToken);

	public Task TogglePlayPauseAsync(CancellationToken cancellationToken = default)
		=> Guard(client => CurrentState.PlaybackState == PlaybackState.Playing
				? client.Player.PausePlayback(cancellationToken)
				: client.Player.ResumePlayback(cancellationToken),
			cancellationToken);

	public Task NextAsync(CancellationToken cancellationToken = default)
		=> Guard(client => client.Player.SkipNext(cancellationToken), cancellationToken);

	public Task PreviousAsync(CancellationToken cancellationToken = default)
		=> Guard(client => client.Player.SkipPrevious(cancellationToken), cancellationToken);

	public Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default)
		=> Guard(client => client.Player.SeekTo(new PlayerSeekToRequest((long)position.TotalMilliseconds),
				cancellationToken),
			cancellationToken);

	public Task SetVolumeAsync(int volumePercent, CancellationToken cancellationToken = default)
		=> Guard(client => client.Player.SetVolume(new PlayerVolumeRequest(Math.Clamp(volumePercent, 0, 100)),
				cancellationToken),
			cancellationToken);

	public Task SetShuffleAsync(bool enabled, CancellationToken cancellationToken = default)
		=> Guard(client => client.Player.SetShuffle(new PlayerShuffleRequest(enabled), cancellationToken),
			cancellationToken);

	public Task SetRepeatModeAsync(RepeatMode mode, CancellationToken cancellationToken = default)
		=> Guard(client => client.Player.SetRepeat(new PlayerSetRepeatRequest(ToRepeatState(mode)), cancellationToken),
			cancellationToken);

	public Task PlayItemAsync(MusicPlayerCatalogItem item, CancellationToken cancellationToken = default)
	{
		var uri = ToSpotifyUri(item);
		return Guard(client => item.Kind == MusicPlayerCatalogItemKind.Playlist
				? client.Player.ResumePlayback(new PlayerResumePlaybackRequest { ContextUri = uri }, cancellationToken)
				: client.Player.ResumePlayback(new PlayerResumePlaybackRequest { Uris = [uri] }, cancellationToken),
			cancellationToken);
	}

	public async Task<IReadOnlyList<MusicPlayerCatalogItem>> GetCatalogAsync(
		string instanceId,
		MusicPlayerCatalogItemKind kind,
		string? filter,
		CancellationToken cancellationToken)
	{
		// Failures are rethrown, not turned into an empty list: the host has to be able to tell
		// "your library is empty" from "we could not read your library" (see IMusicPlayerCatalogProvider).
		// The auth failure is still registered on the way out, because that drives the integration issue.
		ThrowIfLimited("read your library");
		try
		{
			using var scope = SpotifyRequestScope.Interactive("catalog-read");
			var client = await PrepareAsync(cancellationToken) ??
				throw new InvalidOperationException("Spotify is not connected.");

			return kind == MusicPlayerCatalogItemKind.Playlist
				? await GetPlaylistsAsync(client, filter, cancellationToken)
				: await GetTracksAsync(client, filter, cancellationToken);
		}
		catch (SpotifyAuthRejectedException ex)
		{
			RegisterAuthFailure(ex);
			throw;
		}
		catch (APIUnauthorizedException ex)
		{
			RegisterAuthFailure(ex);
			throw;
		}
		catch (Exception ex) when (IsInvalidGrant(ex))
		{
			RegisterAuthFailure(ex);
			throw;
		}
		catch (Exception ex) when (TryNoteApiLimit(ex))
		{
			throw;
		}
	}

	private void ThrowIfLimited(string what)
	{
		if (!ReadsPaused)
		{
			return;
		}

		throw new SpotifyThrottledException(
			$"Spotify is limiting this Spotify app, so Macro Deck cannot {what} until " +
			$"{_apiLimit?.PausedUntil.ToLocalTime():HH:mm}.");
	}

	public async Task<IReadOnlyList<MusicPlayerDevice>> GetDevicesAsync(CancellationToken cancellationToken)
	{
		// Failures are rethrown, not turned into an empty list: the host has to be able to tell
		// "you have no devices" from "we could not read your devices" (see IMusicPlayerDeviceProvider).
		// The auth failure is still registered on the way out, because that drives the integration issue.
		ThrowIfLimited("read your devices");
		try
		{
			using var scope = SpotifyRequestScope.Interactive("device-read");
			var client = await PrepareAsync(cancellationToken) ??
				throw new InvalidOperationException("Spotify is not connected.");

			var response = await client.Player.GetAvailableDevices(cancellationToken);
			return MapDevices(response.Devices);
		}
		catch (SpotifyAuthRejectedException ex)
		{
			RegisterAuthFailure(ex);
			throw;
		}
		catch (APIUnauthorizedException ex)
		{
			RegisterAuthFailure(ex);
			throw;
		}
		catch (Exception ex) when (IsInvalidGrant(ex))
		{
			RegisterAuthFailure(ex);
			throw;
		}
		catch (Exception ex) when (TryNoteApiLimit(ex))
		{
			throw;
		}
	}

	public Task TransferPlaybackAsync(string deviceId, bool startPlayback, CancellationToken cancellationToken)
	{
		var request = new PlayerTransferPlaybackRequest([deviceId]);
		if (startPlayback)
		{
			request.Play = true;
		}

		return Guard(client => client.Player.TransferPlayback(request, cancellationToken), cancellationToken);
	}

	// Restricted devices (e.g. another app's Connect session in a locked-down mode) reject every Web
	// API command, so listing them would offer a choice that cannot work - same spirit as MapTracks
	// dropping unplayable entries. Static is fine here - unlike MapTracks/MapPlaylists this needs no
	// RegisterArtwork.
	internal static IReadOnlyList<MusicPlayerDevice> MapDevices(IEnumerable<Device?> devices)
		=> devices
			.Where(d => d?.Id is not null && !d.IsRestricted)
			.Select(d => new MusicPlayerDevice(d!.Id, d.Name, d.Type, d.IsActive, d.VolumePercent))
			.ToList();

	private static string ToSpotifyUri(MusicPlayerCatalogItem item)
		=> item.Id.StartsWith("spotify:", StringComparison.Ordinal)
			? item.Id
			: $"spotify:{(item.Kind == MusicPlayerCatalogItemKind.Playlist ? "playlist" : "track")}:{item.Id}";

	private async Task<IReadOnlyList<MusicPlayerCatalogItem>> GetTracksAsync(
		SpotifyClient client,
		string? filter,
		CancellationToken cancellationToken)
	{
		IEnumerable<FullTrack?> tracks;
		if (!string.IsNullOrWhiteSpace(filter))
		{
			var search = await client.Search.Item(new SearchRequest(SearchRequest.Types.Track, filter),
				cancellationToken);
			tracks = search.Tracks.Items ?? [];
		}
		else
		{
			var liked = await client.Library.GetTracks(cancellationToken);
			tracks = liked.Items?.Select(s => s.Track) ?? [];
		}

		return MapTracks(tracks);
	}

	private async Task<IReadOnlyList<MusicPlayerCatalogItem>> GetPlaylistsAsync(
		SpotifyClient client,
		string? filter,
		CancellationToken cancellationToken)
	{
		IEnumerable<FullPlaylist?> playlists;
		if (!string.IsNullOrWhiteSpace(filter))
		{
			var search = await client.Search.Item(new SearchRequest(SearchRequest.Types.Playlist, filter),
				cancellationToken);
			playlists = search.Playlists.Items ?? [];
		}
		else
		{
			var page = await client.Playlists.CurrentUsers(cancellationToken);
			playlists = page.Items ?? [];
		}

		return MapPlaylists(playlists);
	}

	internal IReadOnlyList<MusicPlayerCatalogItem> MapTracks(IEnumerable<FullTrack?> tracks)
		=> tracks.Where(t => t?.Id is not null).Select(t => MapTrack(t!)).ToList();

	internal IReadOnlyList<MusicPlayerCatalogItem> MapPlaylists(IEnumerable<FullPlaylist?> playlists)
		=> playlists.Where(p => p?.Id is not null).Select(p => MapPlaylist(p!)).ToList();

	private MusicPlayerCatalogItem MapTrack(FullTrack track)
		=> new(track.Id,
			track.Name,
			MusicPlayerCatalogItemKind.Track,
			track.Artists.Count > 0 ? string.Join(", ", track.Artists.Select(a => a.Name)) : null,
			RegisterArtwork(track.Album.Images),
			track.DurationMs > 0 ? TimeSpan.FromMilliseconds(track.DurationMs) : null);

	private MusicPlayerCatalogItem MapPlaylist(FullPlaylist playlist)
		=> new(playlist.Id!,
			playlist.Name ?? string.Empty,
			MusicPlayerCatalogItemKind.Playlist,
			playlist.Owner?.DisplayName,
			RegisterArtwork(playlist.Images));

	public Task ToggleLikedAsync(string mode, CancellationToken cancellationToken = default)
	{
		var item = _currentItem;
		if (item is null)
		{
			_logger.Debug("Spotify toggle-liked ignored: nothing is playing");
			return Task.CompletedTask;
		}

		return Guard(async client =>
			{
				if (_connector is not { } connector)
				{
					return;
				}

				var save = mode switch
				{
					"add" => true,
					"remove" => false,
					_ => !await IsSavedNowAsync(client, item.Uri, cancellationToken)
				};

				var uris = new Dictionary<string, string>(StringComparer.Ordinal) { ["uris"] = item.Uri };
				await (save
					? connector.Put(SpotifyUrls.Library(), uris, null, cancellationToken)
					: connector.Delete(SpotifyUrls.Library(), uris, null, cancellationToken));

				ForgetSavedState();
			},
			cancellationToken,
			playbackMutation: false,
			reason: "library-liked-mutation");
	}

	private static async Task<bool> IsSavedNowAsync(SpotifyClient client,
		string uri,
		CancellationToken cancellationToken)
	{
		var result = await client.Library.CheckItems(new LibraryCheckItemsRequest([uri]), cancellationToken);
		return result.Count > 0 && result[0];
	}

	public Task ChangePlaylistMembershipAsync(string playlist,
		string mode,
		CancellationToken cancellationToken = default)
	{
		var item = _currentItem;
		if (item is null)
		{
			_logger.Debug("Spotify playlist-membership ignored: nothing is playing");
			return Task.CompletedTask;
		}

		var playlistId = NormalizePlaylistId(playlist);
		if (playlistId is null)
		{
			_logger.Warning("Spotify playlist-membership input could not be resolved to a playlist id");
			return Task.CompletedTask;
		}

		return Guard(async client =>
			{
				await (mode == "remove"
					? client.Playlists.RemovePlaylistItems(playlistId,
						new PlaylistRemoveItemsRequestV2
							{ Items = [new PlaylistRemoveItemsRequestV2.Item { Uri = item.Uri }] },
						cancellationToken)
					: (Task)client.Playlists.AddPlaylistItems(playlistId,
						new PlaylistAddItemsRequest([item.Uri]),
						cancellationToken));

				ForgetPlaylistMembership(playlistId);
			},
			cancellationToken,
			playbackMutation: false,
			reason: "playlist-membership-mutation");
	}

	private static string? NormalizePlaylistId(string value)
	{
		var id = ExtractPlaylistId(value.Trim());
		return string.IsNullOrEmpty(id) ? null : id;
	}

	private static string? ExtractPlaylistId(string trimmed)
	{
		if (trimmed.StartsWith("spotify:playlist:", StringComparison.Ordinal))
		{
			return trimmed["spotify:playlist:".Length..];
		}

		if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) &&
			uri.Host.Contains("open.spotify.com", StringComparison.OrdinalIgnoreCase))
		{
			var segments = uri.AbsolutePath.Trim('/').Split('/');
			var index = Array.IndexOf(segments, "playlist");
			return index >= 0 && index + 1 < segments.Length ? segments[index + 1] : null;
		}

		return trimmed;
	}

	private async Task Guard(Func<SpotifyClient, Task> command,
		CancellationToken cancellationToken,
		bool playbackMutation = true,
		string reason = "playback-action")
	{
		if (SkipCommandWhileLimited())
		{
			return;
		}

		var actionAttempted = false;
		var callerCancelled = false;
		try
		{
			var client = await PrepareAsync(cancellationToken);
			if (client is null)
			{
				return;
			}

			actionAttempted = true;
			using var scope = SpotifyRequestScope.Interactive(reason);
			await command(client);

			_premiumRequired = false;
		}
		catch (SpotifyAuthRejectedException ex)
		{
			RegisterAuthFailure(ex);
			LogCommandFailure(ex, "command");
		}
		catch (SpotifyAuthTransientException ex)
		{
			_logger.Warning("Spotify command skipped because token refresh failed temporarily ({Failure})",
				SafeFailure(ex));
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			callerCancelled = true;
			_logger.Debug("Spotify command was cancelled");
		}
		catch (Exception ex) when (TryNoteApiLimit(ex))
		{
		}
		catch (Exception ex)
		{
			NoteCommandFailure(ex, "command");
		}
		finally
		{
			if (playbackMutation && actionAttempted && !callerCancelled)
			{
				RequestActionPollBurst();
			}
		}
	}

	private bool SkipCommandWhileLimited()
	{
		if (!ReadsPaused)
		{
			return false;
		}

		_logger.Warning("Spotify command skipped: {Reason} until {ResumesAt:HH:mm:ss}",
			_apiLimit?.Kind == SpotifyApiLimitKind.Quota
				? "this Spotify app's quota is exhausted"
				: "Spotify is limiting this app",
			_apiLimit?.PausedUntil.ToLocalTime());
		return true;
	}

	private bool TryNoteApiLimit(Exception ex)
	{
		if (SpotifyApiLimits.IsRateLimited(ex))
		{
			PauseReads(SpotifyApiLimitKind.RateLimit, SpotifyApiLimits.PauseFor(ex, _rateLimitPause), ex);
			return true;
		}

		if (SpotifyApiLimits.IsQuotaExceeded(ex))
		{
			PauseReads(SpotifyApiLimitKind.Quota, _quotaPause, ex);
			return true;
		}

		return false;
	}

	private void NoteCommandFailure(Exception ex, string what)
	{
		// Spotify only admits that an account cannot control playback when it refuses a command, so this
		// is where the premium-required issue is learned. Latched until a command succeeds: the refusal
		// is about the subscription, not this one button press.
		if (SpotifyPremiumRequirement.IsRefusal(ex) && !_premiumRequired)
		{
			_premiumRequired = true;
			_logger.Warning("Spotify refused {What}: the account is not Premium", what);
		}

		LogCommandFailure(ex, what);
	}

	private void LogCommandFailure(Exception ex, string what)
	{
		// A 403/404 here usually means "no active device" or "Spotify Premium required" - log,
		// don't throw, so an action flow continues gracefully.
		if (ex is APIException apiException)
		{
			_logger.Warning("Spotify {What} failed ({Failure})", what, SafeFailure(apiException));
			return;
		}

		_logger.Error("Spotify {What} failed ({Failure})", what, SafeFailure(ex));
	}

	// A token rejection here must reach GetStateAsync's handler; swallowing it would report a
	// connected idle state with a dead token and re-log the failure on every poll.
	//
	// This answers a different question than GetDevicesAsync/MapDevices and must keep its own
	// swallow-and-degrade behaviour: it deliberately does not filter restricted devices, so the
	// now-playing state may name a device the device picker will not list.
	private async Task<MusicPlayerState> BuildIdleStateAsync(
		SpotifyClient client,
		CancellationToken cancellationToken)
	{
		var now = _time.GetUtcNow().UtcDateTime;
		if (_idleState is { } cached && now - _idleStateAtUtc < _idleDevicesTtl)
		{
			return cached;
		}

		if (now < _idleRetryNotBeforeUtc)
		{
			return _idleState ?? new MusicPlayerState { IsConnected = true, PlaybackState = PlaybackState.Stopped };
		}

		try
		{
			var devices = await client.Player.GetAvailableDevices(cancellationToken);
			var device = devices.Devices.FirstOrDefault(d => d.IsActive) ?? devices.Devices.FirstOrDefault();

			var idle = new MusicPlayerState
			{
				IsConnected = true,
				PlaybackState = PlaybackState.Stopped,
				VolumePercent = device?.VolumePercent,
				DeviceName = device?.Name,
				DeviceType = device?.Type
			};
			_idleState = idle;
			_idleStateAtUtc = _time.GetUtcNow().UtcDateTime;
			return idle;
		}
		catch (APIUnauthorizedException)
		{
			throw;
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception ex)
		{
			_idleRetryNotBeforeUtc = _time.GetUtcNow().UtcDateTime + _idleFailureBackoff;
			_logger.Warning("Failed to read Spotify devices ({Failure})", SafeFailure(ex));
			return new MusicPlayerState { IsConnected = true, PlaybackState = PlaybackState.Stopped };
		}
	}

	private void RegisterAuthFailure(Exception ex)
	{
		if (!_accessTokenRejected)
		{
			_accessTokenRejected = true;
			_logger.Warning("Spotify rejected the stored credentials (reconnect to re-authorize; {Failure})",
				SafeFailure(ex));
			return;
		}

		_logger.Debug("Spotify credentials still rejected ({Failure})", SafeFailure(ex));
	}

	private void ClearAuthFailure()
	{
		if (!_accessTokenRejected)
		{
			return;
		}

		_accessTokenRejected = false;
		_logger.Information("Spotify access token accepted again");
	}

	public async Task<bool?> IsCurrentItemSavedAsync(CancellationToken cancellationToken = default)
	{
		if (_savedLatchedOff)
		{
			return null;
		}

		var item = _currentItem;
		if (item is null)
		{
			return null;
		}

		lock (_savedLock)
		{
			var now = _time.GetUtcNow().UtcDateTime;
			if (_savedUri == item.Uri && now - _savedAtUtc < _savedTtl)
			{
				return _savedValue;
			}

			// The failure backoff is inside the lock with the success TTL because it answers the same
			// question - may this spend a request right now - and used to be missing entirely.
			if (now < _savedRetryNotBeforeUtc)
			{
				return null;
			}
		}

		if (ReadsPaused)
		{
			return null;
		}

		try
		{
			using var scope = SpotifyRequestScope.Poll("liked-state");
			var client = await PrepareAsync(cancellationToken);
			if (client is null)
			{
				return null;
			}

			var saved = await IsSavedNowAsync(client, item.Uri, cancellationToken);
			lock (_savedLock)
			{
				_savedUri = item.Uri;
				_savedValue = saved;
				_savedAtUtc = _time.GetUtcNow().UtcDateTime;
			}

			return saved;
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			return null;
		}
		catch (Exception ex) when (TryNoteApiLimit(ex))
		{
			BackOffSavedState();
			return null;
		}
		catch (Exception ex)
		{
			if (IsForbidden(ex) && !SpotifyApiLimits.IsQuotaExceeded(ex))
			{
				LatchSavedStateOff();
			}
			else
			{
				BackOffSavedState();
				_logger.Debug("Failed to read Spotify liked state ({Failure})", SafeFailure(ex));
			}

			return null;
		}
	}

	private void BackOffSavedState()
	{
		lock (_savedLock)
		{
			_savedRetryNotBeforeUtc = _time.GetUtcNow().UtcDateTime + _savedFailureBackoff;
		}
	}

	/// <summary>
	/// Whether the currently playing item is in <paramref name="playlist" />, or <c>null</c> when that
	/// cannot be answered right now - nothing playing, no usable playlist value, the playlist is longer
	/// than the walk cap, or the API is unreachable or rate limited.
	/// </summary>
	public async Task<bool?> IsCurrentItemInPlaylistAsync(string? playlist,
		CancellationToken cancellationToken = default)
	{
		var item = _currentItem;
		if (item is null || string.IsNullOrWhiteSpace(playlist))
		{
			return null;
		}

		var playlistId = NormalizePlaylistId(playlist);
		if (playlistId is null)
		{
			return null;
		}

		lock (_playlistMembershipLock)
		{
			var now = _time.GetUtcNow().UtcDateTime;
			if (_playlistMembership.TryGetValue(playlistId, out var cached) &&
				now - cached.FetchedAtUtc < _playlistMembershipTtl)
			{
				return cached.Uris.Contains(item.Uri);
			}

			if (now < _playlistMembershipRetryNotBeforeUtc)
			{
				return null;
			}
		}

		if (ReadsPaused)
		{
			return null;
		}

		try
		{
			using var scope = SpotifyRequestScope.Poll("playlist-membership");
			var client = await PrepareAsync(cancellationToken);
			if (client is null)
			{
				return null;
			}

			var uris = await ReadPlaylistItemUrisAsync(client, playlistId, cancellationToken);
			if (uris is null)
			{
				BackOffPlaylistMembership();
				return null;
			}

			lock (_playlistMembershipLock)
			{
				_playlistMembership[playlistId] = new PlaylistMembership(uris, _time.GetUtcNow().UtcDateTime);
			}

			return uris.Contains(item.Uri);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			return null;
		}
		catch (Exception ex) when (TryNoteApiLimit(ex))
		{
			BackOffPlaylistMembership();
			return null;
		}
		catch (Exception ex)
		{
			BackOffPlaylistMembership();
			_logger.Debug("Failed to read Spotify playlist membership ({Failure})", SafeFailure(ex));
			return null;
		}
	}

	private static async Task<HashSet<string>?> ReadPlaylistItemUrisAsync(
		SpotifyClient client,
		string playlistId,
		CancellationToken cancellationToken)
	{
		var uris = new HashSet<string>(StringComparer.Ordinal);
		for (var offset = 0; offset < PlaylistMembershipMaxItems; offset += PlaylistMembershipPageSize)
		{
			var page = await client.Playlists.GetPlaylistItems(playlistId,
				new PlaylistGetItemsRequest { Limit = PlaylistMembershipPageSize, Offset = offset },
				cancellationToken);

			if (page.Total > PlaylistMembershipMaxItems)
			{
				return null;
			}

			foreach (var uri in (page.Items ?? []).Select(entry => SpotifyPlayingItem.From(entry.Track)?.Uri))
			{
				if (uri is not null)
				{
					uris.Add(uri);
				}
			}

			if (page.Items is null || page.Items.Count < PlaylistMembershipPageSize)
			{
				break;
			}
		}

		return uris;
	}

	private void BackOffPlaylistMembership()
	{
		lock (_playlistMembershipLock)
		{
			_playlistMembershipRetryNotBeforeUtc = _time.GetUtcNow().UtcDateTime + _playlistMembershipFailureBackoff;
		}
	}

	public void ForgetPlaylistMembership(string? playlist)
	{
		var playlistId = playlist is null ? null : NormalizePlaylistId(playlist);
		lock (_playlistMembershipLock)
		{
			if (playlistId is null)
			{
				_playlistMembership.Clear();
			}
			else
			{
				_playlistMembership.Remove(playlistId);
			}

			_playlistMembershipRetryNotBeforeUtc = DateTime.MinValue;
		}
	}

	private sealed record PlaylistMembership(HashSet<string> Uris, DateTime FetchedAtUtc);

	public void ForgetSavedState()
	{
		lock (_savedLock)
		{
			_savedUri = null;
			_savedValue = null;
			_savedRetryNotBeforeUtc = DateTime.MinValue;
		}
	}

	private void LatchSavedStateOff()
	{
		if (_savedLatchedOff)
		{
			return;
		}

		_savedLatchedOff = true;
		_logger.Warning("Spotify rejected the Liked Songs check (403); no longer polling it");
	}

	private static bool IsForbidden(Exception ex)
		=> ex is APIException { Response: { } response } && (int)response.StatusCode == 403;

	public SpotifyTopItems? GetTopItems()
	{
		if (!_topItemsLatchedOff &&
			!ReadsPaused &&
			_time.GetUtcNow().UtcTicks >= Interlocked.Read(ref _topItemsNextFetchTicks) &&
			Interlocked.CompareExchange(ref _topItemsFetching, 1, 0) == 0)
		{
			_ = RefreshTopItemsAsync();
		}

		return _topItems;
	}

	private async Task RefreshTopItemsAsync()
	{
		// Its own CTS, not a caller's token: one variable's cancellation must not kill the snapshot the
		// other three still need.
		var cts = new CancellationTokenSource(_topItemsFetchTimeout);
		try
		{
			using var scope = SpotifyRequestScope.Poll("top-items");
			var client = await PrepareAsync(cts.Token);
			if (client is null)
			{
				ScheduleNextTopItemsAttempt(_topItemsFailureBackoff);
				return;
			}

			var request = new PersonalizationTopRequest
			{
				TimeRangeParam = PersonalizationTopRequest.TimeRange.LongTerm, Limit = 5
			};
			var tracksTask = client.Personalization.GetTopTracks(request, cts.Token);
			var artistsTask = client.Personalization.GetTopArtists(request, cts.Token);
			await Task.WhenAll(tracksTask, artistsTask);

			var tracks = (tracksTask.Result.Items ?? []).Where(t => t is not null).Select(t => t!.Name).ToList();
			var artists = (artistsTask.Result.Items ?? []).Where(a => a is not null).Select(a => a!.Name).ToList();

			_topItems = new SpotifyTopItems(tracks.Count > 0 ? tracks[0] : null,
				artists.Count > 0 ? artists[0] : null,
				tracks,
				artists);
			ScheduleNextTopItemsAttempt(_topItemsTtl);
		}
		catch (Exception ex)
		{
			if (IsForbidden(ex) && !SpotifyApiLimits.IsQuotaExceeded(ex))
			{
				// Missing user-top-read; retrying would just repeat the 403 forever. A quota 403 is
				// deliberately excluded - that one lifts again, so it only earns the backoff below.
				_topItemsLatchedOff = true;
				_logger.Warning("Spotify rejected the top-items request (403); no longer polling it");
			}
			else
			{
				_logger.Debug("Failed to refresh Spotify top-items snapshot ({Failure})", SafeFailure(ex));
				ScheduleNextTopItemsAttempt(_topItemsFailureBackoff);
			}
		}
		finally
		{
			Interlocked.Exchange(ref _topItemsFetching, 0);
			cts.Dispose();
		}
	}

	private void ScheduleNextTopItemsAttempt(TimeSpan delay)
		=> Interlocked.Exchange(ref _topItemsNextFetchTicks, _time.GetUtcNow().Add(delay).UtcTicks);

	[SuppressMessage("ReSharper", "ConditionalAccessQualifierIsNonNullableAccordingToAPIContract")]
	private MusicPlayerState Map(CurrentlyPlayingContext playback)
	{
		string? trackName = null;
		var artists = new List<string>();
		string? albumName = null;
		string? artworkId = null;
		TimeSpan? duration = null;

		if (playback.Item is FullTrack track)
		{
			trackName = track.Name;
			artists = track.Artists.Select(a => a.Name).ToList();
			albumName = track.Album.Name;
			duration = TimeSpan.FromMilliseconds(track.DurationMs);
			artworkId = RegisterArtwork(track.Album.Images);
		}
		else if (playback.Item is FullEpisode episode)
		{
			trackName = episode.Name;
			artists = [];
			albumName = episode.Show.Name;
			duration = TimeSpan.FromMilliseconds(episode.DurationMs);
			artworkId = RegisterArtwork(episode.Images);
		}

		// Spotify can omit the device object (e.g. right after a transfer); a missing device must not
		// NRE the whole poll into an Error + Disconnected tick.
		var device = playback.Device;

		return new MusicPlayerState
		{
			IsConnected = true,
			PlaybackState = playback.IsPlaying ? PlaybackState.Playing : PlaybackState.Paused,
			TrackName = trackName,
			Artists = artists,
			AlbumName = albumName,
			ArtworkId = artworkId,
			Position = TimeSpan.FromMilliseconds(playback.ProgressMs),
			Duration = duration,
			VolumePercent = device?.VolumePercent,
			ShuffleEnabled = playback.ShuffleState,
			RepeatMode = FromRepeatState(playback.RepeatState),
			DeviceName = device?.Name,
			DeviceType = device?.Type
		};
	}

	private string? RegisterArtwork(IReadOnlyList<Image>? images)
	{
		var url = images?.OrderByDescending(i => i.Width).FirstOrDefault()?.Url;
		if (string.IsNullOrEmpty(url))
		{
			return null;
		}

		var id = Hash(url);
		_artworkUrls[id] = url;
		return id;
	}

	private static string Hash(string value)
	{
		var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
		return Convert.ToHexString(bytes[..8]).ToLowerInvariant();
	}

	private static RepeatMode FromRepeatState(string? state) => state switch
	{
		"track" => RepeatMode.Track,
		"context" => RepeatMode.Context,
		_ => RepeatMode.Off
	};

	private static PlayerSetRepeatRequest.State ToRepeatState(RepeatMode mode) => mode switch
	{
		RepeatMode.Track => PlayerSetRepeatRequest.State.Track,
		RepeatMode.Context => PlayerSetRepeatRequest.State.Context,
		_ => PlayerSetRepeatRequest.State.Off
	};
}
