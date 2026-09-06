using System.Diagnostics;
using MacroDeck.Sdk.Logging;
using Serilog;

namespace MacroDeckHost.Integrations.Spotify;

internal sealed record SpotifyTokenSnapshot(
	string AccessToken,
	string RefreshToken,
	DateTime ExpiresAtUtc,
	DateTime RefreshAtUtc)
{
	internal static SpotifyTokenSnapshot FromStored(
		string accessToken,
		string refreshToken,
		DateTime expiresAtUtc,
		TimeSpan margin)
		=> new(accessToken, refreshToken, expiresAtUtc, expiresAtUtc - margin);

	internal static SpotifyTokenSnapshot FromIssued(
		string accessToken,
		string refreshToken,
		TimeSpan lifetime,
		TimeSpan margin,
		DateTime? issuedAtUtc = null)
	{
		var expiresAtUtc = (issuedAtUtc ?? DateTime.UtcNow) + lifetime;
		var effective = lifetime > TimeSpan.Zero && lifetime < margin * 2 ? lifetime / 2 : margin;
		return new SpotifyTokenSnapshot(accessToken, refreshToken, expiresAtUtc, expiresAtUtc - effective);
	}
}

internal interface ISpotifyAccessTokenSource
{
	string AccessToken { get; }
}

internal sealed class SpotifyTokenManager : ISpotifyAccessTokenSource, IDisposable
{
	internal static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(5);

	private static readonly TimeSpan _defaultRefreshLifetime = TimeSpan.FromSeconds(8);

	private static readonly TimeSpan _defaultFailureBackoff = TimeSpan.FromSeconds(3);
	private static readonly TimeSpan _maximumFailureBackoff = TimeSpan.FromSeconds(60);

	// How long a refresh waits for its rotation to become durable before reporting success anyway.
	// Generous - a healthy write lands in milliseconds - because a timeout here means the caller may
	// hold a token whose rotation never landed (#93). The write itself is never abandoned.
	private static readonly TimeSpan _defaultPersistenceBudget = TimeSpan.FromSeconds(15);

	private readonly Lock _sync = new();
	private readonly Guid _entryId;
	private readonly string _clientId;
	private readonly string _clientSecret;
	private readonly ISpotifyOAuthClient _oauth;
	private readonly SpotifyTokenPersister _persister;
	private readonly ILogger _logger;
	private readonly TimeSpan _refreshLifetime;
	private readonly TimeSpan _failureBackoff;
	private readonly TimeSpan _persistenceBudget;
	private readonly TimeProvider _time;

	private readonly FailureEpisodeTracker _refreshFailures = new();

	private volatile SpotifyTokenSnapshot _tokens;
	private volatile SpotifyAuthenticationState _state = SpotifyAuthenticationState.Valid;
	private Task<SpotifyRefreshOutcome>? _refresh;
	private CancellationTokenSource? _refreshCts;
	private DateTime _retryNotBefore = DateTime.MinValue;
	private bool _retryIsRateLimit;
	private int _consecutiveTransientFailures;
	private int _stopping;

	private volatile SpotifyTokenSnapshot? _rejectedSnapshot;

	private SpotifyTokenSnapshot? _forceRefreshProduct;

	public SpotifyTokenManager(
		Guid entryId,
		string clientId,
		string clientSecret,
		SpotifyTokenSnapshot tokens,
		ISpotifyOAuthClient oauth,
		SpotifyTokenPersister persister,
		ILogger logger,
		TimeSpan? refreshLifetime = null,
		TimeSpan? failureBackoff = null,
		TimeSpan? persistenceBudget = null,
		TimeProvider? timeProvider = null)
	{
		_entryId = entryId;
		_clientId = clientId;
		_clientSecret = clientSecret;
		_tokens = tokens;
		_oauth = oauth;
		_persister = persister;
		_logger = logger;
		_refreshLifetime = refreshLifetime ?? _defaultRefreshLifetime;
		_failureBackoff = failureBackoff ?? _defaultFailureBackoff;
		_persistenceBudget = persistenceBudget ?? _defaultPersistenceBudget;
		_time = timeProvider ?? TimeProvider.System;
	}

	public string AccessToken => _tokens.AccessToken;

	public SpotifyAuthenticationState State => _state;

	internal Guid EntryId => _entryId;

	internal DateTimeOffset? UnavailableSince => _refreshFailures.StartedAt;

	public async Task EnsureValidTokenAsync(CancellationToken cancellationToken)
	{
		if (Volatile.Read(ref _stopping) != 0)
		{
			throw new SpotifyAuthTransientException("Spotify token refresh is shutting down.");
		}

		var snapshot = _tokens;
		if (_state == SpotifyAuthenticationState.ReauthorizationRequired)
		{
			throw RejectedAgain();
		}

		if (_time.GetUtcNow().UtcDateTime < snapshot.RefreshAtUtc && !ReferenceEquals(snapshot, _rejectedSnapshot))
		{
			return;
		}

		var waited = Stopwatch.StartNew();
		Task<SpotifyRefreshOutcome> shared;
		lock (_sync)
		{
			if (_state == SpotifyAuthenticationState.ReauthorizationRequired)
			{
				throw RejectedAgain();
			}

			if (_refresh is { IsCompleted: true })
			{
				_refresh = null;
				_refreshCts?.Dispose();
				_refreshCts = null;
			}

			if (_refresh is null)
			{
				if (!ReferenceEquals(_tokens, snapshot))
				{
					return;
				}

				var now = _time.GetUtcNow().UtcDateTime;
				if (now < _retryNotBefore)
				{
					// The reason is carried through the backoff, not just into it: while a rate limit is
					// what closed the endpoint, every caller has to know that - it is what pauses the
					// player's own reads instead of letting them keep asking for a token they cannot get.
					throw new SpotifyAuthTransientException(
						"Spotify's token endpoint recently failed; retrying shortly.")
					{
						IsRateLimit = _retryIsRateLimit, RetryAfter = _retryNotBefore - now
					};
				}

				if (Volatile.Read(ref _stopping) != 0)
				{
					throw new SpotifyAuthTransientException("Spotify token refresh is shutting down.");
				}

				_refreshCts = new CancellationTokenSource(_refreshLifetime);
				_refresh = RefreshCoreAsync(snapshot, _refreshCts.Token);
			}

			shared = _refresh;
		}

		var outcome = await shared.WaitAsync(cancellationToken);
		_logger.Debug("Spotify token wait for entry {EntryId} finished after {ElapsedMs} ms with {Outcome}",
			_entryId,
			waited.ElapsedMilliseconds,
			outcome.Result);

		switch (outcome.Result)
		{
			case SpotifyRefreshResult.Success:
				return;
			case SpotifyRefreshResult.Rejected:
				throw new SpotifyAuthRejectedException(outcome.Message, outcome.Cause!);
			default:
				throw Transient(outcome);
		}
	}

	private static SpotifyAuthTransientException Transient(SpotifyRefreshOutcome outcome)
		=> new(outcome.Message, outcome.Cause!)
		{
			IsRateLimit = outcome.Cause is SpotifyAuthTransientException { IsRateLimit: true },
			RetryAfter = (outcome.Cause as SpotifyAuthTransientException)?.RetryAfter
		};

	public async Task<bool> TryRefreshRejectedAccessTokenAsync(
		string rejectedAccessToken,
		CancellationToken cancellationToken)
	{
		SpotifyTokenSnapshot marked;
		lock (_sync)
		{
			var current = _tokens;
			if (!string.Equals(current.AccessToken, rejectedAccessToken, StringComparison.Ordinal))
			{
				return true;
			}

			if (ReferenceEquals(current, _forceRefreshProduct))
			{
				return false;
			}

			marked = current;
			_rejectedSnapshot = current;
		}

		await EnsureValidTokenAsync(cancellationToken);

		lock (_sync)
		{
			_forceRefreshProduct = _tokens;
			if (ReferenceEquals(_rejectedSnapshot, marked))
			{
				_rejectedSnapshot = null;
			}
		}

		return true;
	}

	internal void NoteAccessTokenAccepted()
	{
		lock (_sync)
		{
			_forceRefreshProduct = null;
			_rejectedSnapshot = null;
		}
	}

	public async Task StopAsync(TimeSpan timeout)
	{
		Interlocked.Exchange(ref _stopping, 1);

		Task<SpotifyRefreshOutcome>? inFlight;
		lock (_sync)
		{
			inFlight = _refresh;
		}

		if (inFlight is not null)
		{
			try
			{
				await inFlight.WaitAsync(timeout);
			}
			catch (TimeoutException)
			{
				_logger.Warning(
					"A Spotify token refresh for entry {EntryId} did not finish within {Timeout}; a rotated " +
					"refresh token may not have been stored",
					_entryId,
					timeout);
			}
		}

		lock (_sync)
		{
			if (_refresh is null or { IsCompleted: true })
			{
				_refreshCts?.Dispose();
				_refreshCts = null;
			}
		}
	}

	public void Dispose()
	{
		Interlocked.Exchange(ref _stopping, 1);
		lock (_sync)
		{
			if (_refresh is null or { IsCompleted: true })
			{
				_refreshCts?.Dispose();
				_refreshCts = null;
			}
		}
	}

	private SpotifyAuthRejectedException RejectedAgain()
		=> new($"Spotify rejected the stored authorization for entry {_entryId}; reconnect to re-authorize.");

	private async Task<SpotifyRefreshOutcome> RefreshCoreAsync(
		SpotifyTokenSnapshot observed,
		CancellationToken lifetime)
	{
		await Task.Yield();

		var stopwatch = Stopwatch.StartNew();
		var published = false;
		try
		{
			var response = await _oauth.RefreshAsync(_clientId, _clientSecret, observed.RefreshToken, lifetime);
			var refreshed = SpotifyTokenSnapshot.FromIssued(response.AccessToken,
				response.RefreshToken is { Length: > 0 } rotated ? rotated : observed.RefreshToken,
				TimeSpan.FromSeconds(response.ExpiresIn),
				RefreshMargin,
				_time.GetUtcNow().UtcDateTime);

			var persisting = _persister.EnqueueAndWaitAsync(_entryId,
				refreshed.AccessToken,
				response.RefreshToken,
				refreshed.ExpiresAtUtc);

			lock (_sync)
			{
				_tokens = refreshed;
				_state = SpotifyAuthenticationState.Valid;
				_retryNotBefore = DateTime.MinValue;
				_retryIsRateLimit = false;
				_consecutiveTransientFailures = 0;
			}

			published = true;

			// Not bounded by the refresh lifetime - abandoning a rotation mid-write is the permanent token
			// loss issue #93 fixed - but not unbounded either: a wedged config-store write used to pin
			// _refresh forever, and every caller at the next refresh point with it. The write itself stays
			// queued on the persister's FIFO tail; only this refresh stops waiting for it.
			bool stored;
			try
			{
				stored = await persisting.WaitAsync(_persistenceBudget, CancellationToken.None);
			}
			catch (TimeoutException)
			{
				stored = true;
				_logger.Error(
					"A rotated Spotify token for entry {EntryId} is still being written after {Timeout}; the " +
					"write stays queued",
					_entryId,
					_persistenceBudget);
				_ = LogLatePersistenceAsync(persisting);
			}

			if (!stored)
			{
				_logger.Error(
					"A rotated Spotify token for entry {EntryId} could not be stored durably; it will have to " +
					"be re-authorized after a restart",
					_entryId);
			}

			if (_refreshFailures.RecordSuccess() is { } episode)
			{
				_logger.Information(
					"Spotify token refresh for entry {EntryId} recovered after {Duration} ({Failures} failed attempts)",
					_entryId,
					episode.Duration,
					episode.Failures);
			}

			// Information on purpose: roughly one line per hour whose absence from the log file is
			// itself diagnostic - a silent Spotify outage used to leave no durable trace at all.
			_logger.Information("Refreshed the Spotify access token for entry {EntryId} in {ElapsedMs} ms",
				_entryId,
				stopwatch.ElapsedMilliseconds);
			return SpotifyRefreshOutcome.Succeeded;
		}
		catch (SpotifyAuthRejectedException ex)
		{
			lock (_sync)
			{
				_state = SpotifyAuthenticationState.ReauthorizationRequired;
			}

			_logger.Warning(
				"Spotify rejected the stored credentials for entry {EntryId} after {ElapsedMs} ms (reconnect " +
				"to re-authorize; {Failure})",
				_entryId,
				stopwatch.ElapsedMilliseconds,
				SafeFailure(ex));
			return new SpotifyRefreshOutcome(SpotifyRefreshResult.Rejected,
				"Spotify rejected the stored credentials.",
				ex);
		}
		catch (Exception ex)
		{
			// Everything is caught, including the lifetime cancellation, so the task cannot fault.
			if (published)
			{
				// The new token is live; a failure after publication (a persistence throw) must not report
				// the account unavailable for the whole hour that token is valid.
				_logger.Error(ex, "A refreshed Spotify token for entry {EntryId} could not be finalised", _entryId);
				return SpotifyRefreshOutcome.Succeeded;
			}

			lock (_sync)
			{
				var exponent = Math.Min(_consecutiveTransientFailures, 30);
				var exponentialSeconds = _failureBackoff.TotalSeconds * Math.Pow(2, exponent);
				var backoff = TimeSpan.FromSeconds(Math.Min(_maximumFailureBackoff.TotalSeconds,
					exponentialSeconds));
				if (ex is SpotifyAuthTransientException { RetryAfter: { } retryAfter } && retryAfter > backoff)
				{
					backoff = retryAfter;
				}

				_consecutiveTransientFailures++;
				_state = SpotifyAuthenticationState.TemporarilyUnavailable;
				_retryNotBefore = _time.GetUtcNow().UtcDateTime + backoff;
				_retryIsRateLimit = ex is SpotifyAuthTransientException { IsRateLimit: true };
			}

			var signal = _refreshFailures.RecordFailure(SafeFailure(ex));
			switch (signal.Kind)
			{
				case FailureEpisodeSignalKind.Onset:
					_logger.Warning(
						"Spotify token refresh for entry {EntryId} failed after {ElapsedMs} ms; keeping the stored " +
						"credentials and retrying later ({Failure})",
						_entryId,
						stopwatch.ElapsedMilliseconds,
						SafeFailure(ex));
					break;
				case FailureEpisodeSignalKind.SummaryDue:
					_logger.Information(
						"Spotify token refresh for entry {EntryId} still failing after {Duration} ({Failures} " +
						"attempts; last error: {LastError})",
						_entryId,
						signal.Duration,
						signal.ConsecutiveFailures,
						signal.LastError);
					break;
				default:
					_logger.Debug(
						"Spotify token refresh for entry {EntryId} failed after {ElapsedMs} ms; keeping the stored " +
						"credentials and retrying later ({Failure})",
						_entryId,
						stopwatch.ElapsedMilliseconds,
						SafeFailure(ex));
					break;
			}

			return new SpotifyRefreshOutcome(SpotifyRefreshResult.Transient, TransientMessage(ex), ex);
		}
	}

	private async Task LogLatePersistenceAsync(Task<bool> persisting)
	{
		if (!await persisting)
		{
			_logger.Error("A rotated Spotify token for entry {EntryId} could not be stored durably; it will have to " +
				"be re-authorized after a restart",
				_entryId);
		}
	}

	private static string TransientMessage(Exception ex)
		=> ex is OperationCanceledException
			? "The Spotify token refresh did not finish in time."
			: "Spotify token refresh failed temporarily.";

	private static string SafeFailure(Exception ex)
		=> ex.GetType().Name;

	private enum SpotifyRefreshResult
	{
		Success,
		Transient,
		Rejected
	}

	private sealed record SpotifyRefreshOutcome(SpotifyRefreshResult Result, string Message, Exception? Cause)
	{
		internal static readonly SpotifyRefreshOutcome Succeeded
			= new(SpotifyRefreshResult.Success, string.Empty, null);
	}
}
