using MacroDeck.Sdk.Logging;
using MacroDeckHost.Application.Connect;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.Connect;

public sealed class ConnectSessionService : IConnectSessionService, IAsyncDisposable
{
	// The access token lives ten minutes, so the margin has to stay well under half of that: a five
	// minute margin would refresh at the halfway point of every token and double the rotation rate.
	internal static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(1);

	// A restart must not burn a refresh-token rotation for nothing. A credential younger than this is
	// published from cache and left alone until something actually needs a token.
	internal static readonly TimeSpan ColdStartRefreshAge = TimeSpan.FromHours(12);

	internal static readonly TimeSpan SuspensionRetryFloor = TimeSpan.FromMinutes(5);

	internal static readonly TimeSpan[] BackoffSchedule =
	[
		TimeSpan.FromSeconds(2),
		TimeSpan.FromSeconds(15),
		TimeSpan.FromSeconds(45),
		TimeSpan.FromMinutes(2),
		TimeSpan.FromMinutes(5),
		TimeSpan.FromMinutes(15),
		TimeSpan.FromMinutes(30)
	];

	private static readonly TimeSpan _persistenceBudget = TimeSpan.FromSeconds(15);

	private readonly IConnectIdentityClient _identityClient;
	private readonly IConnectCredentialStore _store;
	private readonly ConnectTokenPersister _persister;
	private readonly ConnectSignInFlow _signInFlow;
	private readonly IConnectSuspensionFloor _suspensionFloor;
	private readonly TimeProvider _timeProvider;
	private readonly Func<TimeSpan, CancellationToken, Task> _delay;
	private readonly Func<TimeSpan, TimeSpan> _jitter;
	private readonly ILogger _logger;

	private readonly FailureEpisodeTracker _failures;
	private readonly Lock _sync = new();
	private readonly CancellationTokenSource _lifetime = new();

	// Read once, while the source is certainly alive: the token keeps working after the source is
	// disposed, whereas CancellationTokenSource.Token throws.
	private readonly CancellationToken _lifetimeToken;

	private ConnectSessionSnapshot _snapshot = ConnectSessionSnapshot.SignedOut;
	private ConnectCredential? _credential;
	private string? _accessToken;
	private DateTimeOffset _accessTokenRefreshAt = DateTimeOffset.MinValue;
	private Task<RefreshOutcome>? _refresh;
	private Task? _retryLoop;
	private Task? _signInObserver;
	private TimeSpan? _retryAfter;
	private bool _frozen;
	private bool _disposed;

	public ConnectSessionService(
		IConnectIdentityClient identityClient,
		IConnectCredentialStore store,
		ConnectTokenPersister persister,
		ConnectSignInFlow signInFlow,
		IConnectSuspensionFloor suspensionFloor,
		TimeProvider timeProvider,
		ILogger logger,
		Func<TimeSpan, CancellationToken, Task>? delay = null,
		Func<TimeSpan, TimeSpan>? jitter = null)
	{
		_identityClient = identityClient;
		_store = store;
		_persister = persister;
		_signInFlow = signInFlow;
		_suspensionFloor = suspensionFloor;
		_timeProvider = timeProvider;
		_logger = logger.ForContext<ConnectSessionService>();
		_delay = delay ?? ((span, ct) => Task.Delay(span, ct));
		_jitter = jitter ?? DefaultJitter;
		_failures = new FailureEpisodeTracker(time: timeProvider);
		_lifetimeToken = _lifetime.Token;
	}

	public event EventHandler<ConnectSessionSnapshot>? SessionChanged;

	public ConnectSessionSnapshot Current
	{
		get
		{
			lock (_sync)
			{
				return _snapshot;
			}
		}
	}

	public async Task Initialize(CancellationToken cancellationToken = default)
	{
		var credential = await _store.Load(cancellationToken);
		if (credential is null)
		{
			Publish(ConnectSessionSnapshot.SignedOut);
			return;
		}

		lock (_sync)
		{
			_credential = credential;
		}

		Publish(new ConnectSessionSnapshot(ConnectAccountStatus.SignedIn,
			ConnectConnectivity.Ok,
			new ConnectAccount(credential.Subject,
				credential.CachedDisplayName ?? credential.Subject,
				credential.CachedPictureUrl,
				null,
				[]),
			null,
			null,
			null));

		if (_timeProvider.GetUtcNow() - credential.IssuedAtUtc >= ColdStartRefreshAge &&
			await SuspensionRetryDue(cancellationToken))
		{
			_ = Task.Run(() => KeepAlive(_lifetimeToken), CancellationToken.None);
		}
	}

	public async Task<ConnectSignInStart> StartSignIn(CancellationToken cancellationToken = default)
	{
		ConnectSignInStart start;
		try
		{
			start = await _signInFlow.Begin(cancellationToken);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			// Requesting the device authorization is a network call, so unlike the loopback flow this can
			// fail before there is anything to show. Published rather than only thrown: the caller's request
			// fails, but every client learns why instead of watching the button do nothing.
			_logger.Warning(ex, "Could not start a Macro Deck Connect sign-in");
			Publish(Current with { SignInFailure = ConnectSignInFailure.Unreachable, Message = null });

			throw;
		}

		Publish(Current with
		{
			Status = ConnectAccountStatus.SigningIn,
			Connectivity = ConnectConnectivity.Ok,
			Message = null,
			SignInFailure = null
		});

		lock (_sync)
		{
			if (_signInObserver is null or { IsCompleted: true })
			{
				_signInObserver = Task.Run(ObserveSignIn, CancellationToken.None);
			}
		}

		return start;
	}

	public Task CancelSignIn(CancellationToken cancellationToken = default)
		=> _signInFlow.Cancel(cancellationToken);

	public async Task SignOut(CancellationToken cancellationToken = default)
	{
		ConnectCredential? credential;
		lock (_sync)
		{
			credential = _credential;
			_credential = null;
			_accessToken = null;
			_accessTokenRefreshAt = DateTimeOffset.MinValue;
			_refresh = null;
			_frozen = false;
			_retryAfter = null;
		}

		await _signInFlow.Cancel(cancellationToken);

		if (credential is not null)
		{
			try
			{
				// There is no end-session endpoint for this client; revoking the refresh token is the whole
				// server-side sign-out.
				await _identityClient.Revoke(credential.RefreshToken, cancellationToken);
			}
			catch (Exception ex)
			{
				_logger.Warning(ex, "Revoking the Macro Deck Connect credential failed; ending the session locally");
			}
		}

		await _store.Clear(cancellationToken);
		Publish(ConnectSessionSnapshot.SignedOut);
	}

	public async Task<string> GetAccessToken(CancellationToken cancellationToken = default)
	{
		var snapshot = Current;

		switch (snapshot.Status)
		{
			case ConnectAccountStatus.ReauthenticationRequired:
				throw new ConnectAuthRejectedException(snapshot.Message ??
					"Macro Deck Connect requires a new sign-in.");
			case ConnectAccountStatus.Suspended when !await SuspensionRetryDue(cancellationToken):
				throw new ConnectAccountSuspendedException(snapshot.Message ?? "This account has been suspended.");
			case ConnectAccountStatus.SignedOut or ConnectAccountStatus.SigningIn:
				throw new ConnectAuthRejectedException("No Macro Deck Connect account is signed in.");
		}

		lock (_sync)
		{
			if (_accessToken is { Length: > 0 } cached && _timeProvider.GetUtcNow() < _accessTokenRefreshAt)
			{
				return cached;
			}
		}

		var outcome = await EnsureRefreshed(cancellationToken);

		return outcome.Result switch
		{
			RefreshResult.Success => outcome.AccessToken!,
			RefreshResult.Rejected => throw new ConnectAuthRejectedException(outcome.Message),
			RefreshResult.Suspended => throw new ConnectAccountSuspendedException(outcome.Message),
			_ => throw new ConnectAuthTransientException(outcome.Message)
		};
	}

	public async ValueTask DisposeAsync()
	{
		lock (_sync)
		{
			if (_disposed)
			{
				return;
			}

			_disposed = true;
		}

		await CompleteAsync();
		_lifetime.Dispose();
	}

	/// <summary>
	/// Cancels the service's own lifetime and waits for its background work and any queued credential
	/// write to finish. Safe to call repeatedly and after disposal.
	/// </summary>
	public async Task CompleteAsync()
	{
		if (!_lifetimeToken.IsCancellationRequested)
		{
			try
			{
				await _lifetime.CancelAsync();
			}
			catch (ObjectDisposedException)
			{
				// The container tracks this service once per registration and so disposes it more than once;
				// a lifetime that is already gone has already cancelled everything this call would cancel.
			}
		}

		Task? loop;
		lock (_sync)
		{
			loop = _retryLoop;
		}

		if (loop is not null)
		{
			try
			{
				await loop;
			}
			catch (OperationCanceledException)
			{
			}
		}

		await _persister.CompleteAsync();
	}

	/// <summary>
	/// Forces one refresh attempt, sliding the 180-day window even when nothing else would have asked for
	/// a token. Never throws: a keep-alive failure is already reflected in the published snapshot.
	/// </summary>
	internal async Task KeepAlive(CancellationToken cancellationToken)
	{
		var snapshot = Current;
		if (snapshot.Status is ConnectAccountStatus.SignedOut
			or ConnectAccountStatus.SigningIn
			or ConnectAccountStatus.ReauthenticationRequired)
		{
			return;
		}

		if (snapshot.Status is ConnectAccountStatus.Suspended && !await SuspensionRetryDue(cancellationToken))
		{
			return;
		}

		await EnsureRefreshed(cancellationToken);
	}

	private async Task ObserveSignIn()
	{
		var outcome = await _signInFlow.WaitForOutcome(CancellationToken.None);

		if (outcome.Result is not ConnectSignInResult.Completed)
		{
			await _signInFlow.Cancel(CancellationToken.None);

			if (outcome.Result is ConnectSignInResult.Cancelled)
			{
				Publish(ConnectSessionSnapshot.SignedOut);
				return;
			}

			// The outcome's own message is diagnostic English; clients are told which kind of failure it
			// was and render that themselves.
			_logger.Information("A Macro Deck Connect sign-in ended as {Result}: {Reason}",
				outcome.Result,
				outcome.Message);

			Publish(ConnectSessionSnapshot.SignedOut with
			{
				SignInFailure = outcome.Result switch
				{
					ConnectSignInResult.Denied => ConnectSignInFailure.Denied,
					ConnectSignInResult.Expired => ConnectSignInFailure.Expired,
					ConnectSignInResult.Unreachable => ConnectSignInFailure.Unreachable,
					_ => ConnectSignInFailure.Failed
				}
			});

			return;
		}

		var tokens = outcome.Tokens!;
		var claims = outcome.Claims!;
		var now = _timeProvider.GetUtcNow();
		var credential = new ConnectCredential(tokens.RefreshToken,
			claims.Subject,
			claims.DisplayName,
			claims.PictureUrl,
			now);

		var persisting = _persister.EnqueueAndWaitAsync(credential);

		lock (_sync)
		{
			_credential = credential;
			_accessToken = tokens.AccessToken;
			_accessTokenRefreshAt = now + tokens.ExpiresIn - RefreshMargin;
			_frozen = false;
			_retryAfter = null;
		}

		await AwaitDurability(persisting);
		await _signInFlow.Cancel(CancellationToken.None);

		Publish(new ConnectSessionSnapshot(ConnectAccountStatus.SignedIn,
			ConnectConnectivity.Ok,
			ToAccount(claims),
			null,
			now,
			null));
	}

	private Task<RefreshOutcome> EnsureRefreshed(CancellationToken cancellationToken)
	{
		Task<RefreshOutcome> shared;
		lock (_sync)
		{
			if (_refresh is { IsCompleted: true })
			{
				_refresh = null;
			}

			if (_refresh is null)
			{
				if (_credential is not { } credential)
				{
					return Task.FromResult(new RefreshOutcome(RefreshResult.Rejected,
						null,
						"No Macro Deck Connect account is signed in."));
				}

				// The shared refresh runs on the service's own lifetime, never on a caller's token: one
				// caller walking away must not cancel a rotation every other caller is waiting on.
				_refresh = RefreshCore(credential, _lifetimeToken);
			}

			shared = _refresh;
		}

		return shared.WaitAsync(cancellationToken);
	}

	private async Task<RefreshOutcome> RefreshCore(ConnectCredential observed, CancellationToken cancellationToken)
	{
		await Task.Yield();

		try
		{
			var response = await _identityClient.Refresh(observed.RefreshToken, cancellationToken);
			var claims = ConnectIdTokenReader.Read(response.IdToken, null, _timeProvider);
			var now = _timeProvider.GetUtcNow();
			var rotated = new ConnectCredential(response.RefreshToken,
				claims.Subject,
				claims.DisplayName,
				claims.PictureUrl,
				now);

			var persisting = _persister.EnqueueAndWaitAsync(rotated);

			lock (_sync)
			{
				_credential = rotated;
				_accessToken = response.AccessToken;
				_accessTokenRefreshAt = now + response.ExpiresIn - RefreshMargin;
				_retryAfter = null;
			}

			// The rotated token has to be durable before the access token it came with is handed out: a
			// crash in between would leave the host holding a token whose predecessor is already redeemed.
			await AwaitDurability(persisting);

			if (_failures.RecordSuccess() is { } episode)
			{
				_logger.Information(
					"Macro Deck Connect recovered after {Duration} ({Failures} failed refresh attempts)",
					episode.Duration,
					episode.Failures);
			}

			Publish(new ConnectSessionSnapshot(ConnectAccountStatus.SignedIn,
				ConnectConnectivity.Ok,
				ToAccount(claims),
				null,
				now,
				null));

			return new RefreshOutcome(RefreshResult.Success, response.AccessToken, string.Empty);
		}
		catch (ConnectAuthRejectedException ex)
		{
			await OnRejected(ex);
			return new RefreshOutcome(RefreshResult.Rejected, null, ex.Message);
		}
		catch (ConnectAccountSuspendedException ex)
		{
			await OnSuspended(ex);
			return new RefreshOutcome(RefreshResult.Suspended, null, ex.Message);
		}
		catch (Exception ex)
		{
			OnTransient(ex);
			return new RefreshOutcome(RefreshResult.Transient, null, ex.Message);
		}
	}

	private async Task OnRejected(ConnectAuthRejectedException ex)
	{
		lock (_sync)
		{
			_credential = null;
			_accessToken = null;
			_accessTokenRefreshAt = DateTimeOffset.MinValue;
			_frozen = true;
		}

		_failures.RecordSuccess();
		await _store.Clear(CancellationToken.None);

		_logger.Warning(ex, "Macro Deck Connect rejected the stored credential; a new sign-in is required");

		Publish(Current with
		{
			Status = ConnectAccountStatus.ReauthenticationRequired,
			Connectivity = ConnectConnectivity.Ok,
			OfflineSince = null,
			Message = ex.Message
		});
	}

	private async Task OnSuspended(ConnectAccountSuspendedException ex)
	{
		// A suspension does not revoke the authorization, so the credential is kept and nothing retries
		// automatically: the account recovers by itself once the suspension is lifted, and the persisted
		// floor is what stops a crash loop from hammering the token endpoint until then.
		lock (_sync)
		{
			_accessToken = null;
			_accessTokenRefreshAt = DateTimeOffset.MinValue;
			_frozen = true;
		}

		_failures.RecordSuccess();
		await _suspensionFloor.Write(_timeProvider.GetUtcNow() + SuspensionRetryFloor, CancellationToken.None);

		Publish(Current with
		{
			Status = ConnectAccountStatus.Suspended,
			Connectivity = ConnectConnectivity.Ok,
			OfflineSince = null,
			Message = ex.Message
		});
	}

	private void OnTransient(Exception ex)
	{
		var now = _timeProvider.GetUtcNow();

		lock (_sync)
		{
			_retryAfter = ex is ConnectAuthTransientException { RetryAfter: { } retryAfter } ? retryAfter : null;
		}

		var signal = _failures.RecordFailure(ex.Message);
		switch (signal.Kind)
		{
			case FailureEpisodeSignalKind.Onset:
				_logger.Warning(ex, "Macro Deck Connect is unreachable; keeping the session and retrying");
				break;
			case FailureEpisodeSignalKind.SummaryDue:
				_logger.Information(
					"Macro Deck Connect still unreachable after {Duration} ({Failures} attempts; last: {LastError})",
					signal.Duration,
					signal.ConsecutiveFailures,
					signal.LastError);
				break;
			default:
				_logger.Debug(ex, "A Macro Deck Connect refresh attempt failed");
				break;
		}

		var current = Current;
		Publish(current with
		{
			Connectivity = ConnectConnectivity.Offline, OfflineSince = current.OfflineSince ?? now, Message = null
		});

		StartRetryLoop();
	}

	private void StartRetryLoop()
	{
		lock (_sync)
		{
			if (_frozen || _lifetimeToken.IsCancellationRequested || _retryLoop is { IsCompleted: false })
			{
				return;
			}

			_retryLoop = Task.Run(() => RetryLoop(_lifetimeToken), CancellationToken.None);
		}
	}

	private async Task RetryLoop(CancellationToken cancellationToken)
	{
		var attempt = 0;

		while (!cancellationToken.IsCancellationRequested)
		{
			var scheduled = _jitter(BackoffSchedule[Math.Min(attempt, BackoffSchedule.Length - 1)]);

			TimeSpan? requested;
			lock (_sync)
			{
				requested = _retryAfter;
			}

			// A rate limit's Retry-After is a floor, never a ceiling: the schedule may back off further but
			// must never come back sooner than the server asked for.
			if (requested is { } after && after > scheduled)
			{
				scheduled = after;
			}

			try
			{
				await _delay(scheduled, cancellationToken);
			}
			catch (OperationCanceledException)
			{
				return;
			}

			attempt++;

			var outcome = await EnsureRefreshedForRetry(cancellationToken);
			if (outcome.Result is not RefreshResult.Transient)
			{
				return;
			}
		}
	}

	private async Task<RefreshOutcome> EnsureRefreshedForRetry(CancellationToken cancellationToken)
	{
		try
		{
			return await EnsureRefreshed(cancellationToken);
		}
		catch (OperationCanceledException)
		{
			return new RefreshOutcome(RefreshResult.Rejected, null, "shutting down");
		}
	}

	private async Task AwaitDurability(Task<bool> persisting)
	{
		try
		{
			if (!await persisting.WaitAsync(_persistenceBudget, CancellationToken.None))
			{
				_logger.Error("The rotated Macro Deck Connect credential could not be stored durably");
			}
		}
		catch (TimeoutException)
		{
			// The write is never abandoned or cancelled - only this caller stops waiting for it. Dropping a
			// rotation mid-write is what loses a refresh token permanently.
			_logger.Error("The rotated Macro Deck Connect credential is still being written; it stays queued");
		}
	}

	private async Task<bool> SuspensionRetryDue(CancellationToken cancellationToken)
	{
		var floor = await _suspensionFloor.Read(cancellationToken);

		return floor is null || _timeProvider.GetUtcNow() >= floor.Value;
	}

	private void Publish(ConnectSessionSnapshot next)
	{
		ConnectSessionSnapshot previous;
		lock (_sync)
		{
			previous = _snapshot;
			_snapshot = next;
		}

		// LastSuccessfulRefreshUtc moving on its own is not a transition any client reacts to; broadcasting
		// it would wake every connected UI once an hour for nothing.
		if (previous.Status != next.Status ||
			previous.Connectivity != next.Connectivity ||
			previous.OfflineSince != next.OfflineSince ||
			previous.SignInFailure != next.SignInFailure ||
			!string.Equals(previous.Message, next.Message, StringComparison.Ordinal) ||
			!SameAccount(previous.Account, next.Account))
		{
			SessionChanged?.Invoke(this, next);
		}
	}

	private static bool SameAccount(ConnectAccount? left, ConnectAccount? right)
	{
		if (left is null || right is null)
		{
			return ReferenceEquals(left, right);
		}

		return string.Equals(left.Subject, right.Subject, StringComparison.Ordinal) &&
			string.Equals(left.DisplayName, right.DisplayName, StringComparison.Ordinal) &&
			string.Equals(left.PictureUrl, right.PictureUrl, StringComparison.Ordinal) &&
			string.Equals(left.CreatorUsername, right.CreatorUsername, StringComparison.Ordinal) &&
			left.Roles.SequenceEqual(right.Roles, StringComparer.Ordinal);
	}

	private static ConnectAccount ToAccount(ConnectIdTokenClaims claims)
		=> new(claims.Subject, claims.DisplayName, claims.PictureUrl, claims.CreatorUsername, claims.Roles);

	private static TimeSpan DefaultJitter(TimeSpan span)
		=> span * (0.8 + (Random.Shared.NextDouble() * 0.4));

	private enum RefreshResult
	{
		Success,
		Transient,
		Rejected,
		Suspended
	}

	private sealed record RefreshOutcome(RefreshResult Result, string? AccessToken, string Message);
}
