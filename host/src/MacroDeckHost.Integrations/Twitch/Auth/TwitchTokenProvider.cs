using Serilog;

namespace MacroDeckHost.Integrations.Twitch.Auth;

internal sealed class TwitchTokenProvider : IDisposable
{
	private static readonly TimeSpan _refreshMargin = TimeSpan.FromMinutes(5);

	private readonly Guid _entryId;
	private readonly string _clientId;
	private readonly ITwitchOAuthClient _client;
	private readonly TwitchTokenPersister _persister;
	private readonly ILogger _logger;
	private readonly SemaphoreSlim _gate = new(1, 1);
	private int _stopping;

	private volatile TwitchTokens _tokens;

	public TwitchTokenProvider(
		Guid entryId,
		string clientId,
		TwitchTokens tokens,
		ITwitchOAuthClient client,
		TwitchTokenPersister persister,
		ILogger logger)
	{
		_entryId = entryId;
		_clientId = clientId;
		_tokens = tokens;
		_client = client;
		_persister = persister;
		_logger = logger;
	}

	public bool NeedsReauthorization { get; private set; }

	public IReadOnlyList<string> Scopes => _tokens.Scopes;

	public async Task<string> GetAsync(CancellationToken cancellationToken)
	{
		var observed = _tokens;

		return IsExpiring(observed)
			? await RefreshAsync(observed, cancellationToken)
			: observed.AccessToken;
	}

	public Task<string> ForceRefreshAsync(CancellationToken cancellationToken)
		=> RefreshAsync(_tokens, cancellationToken);

	public async Task<TwitchTokenIdentity> ValidateAsync(CancellationToken cancellationToken)
	{
		var accessToken = await GetAsync(cancellationToken);

		TwitchTokenIdentity identity;
		try
		{
			identity = await _client.ValidateAsync(accessToken, cancellationToken);
		}
		catch (TwitchOAuthRejectedException)
		{
			var refreshed = await ForceRefreshAsync(cancellationToken);
			identity = await _client.ValidateAsync(refreshed, cancellationToken);
		}

		await AdoptScopesAsync(identity.Scopes, cancellationToken);
		return identity;
	}

	public async Task StopAsync()
	{
		Interlocked.Exchange(ref _stopping, 1);
		await _gate.WaitAsync();
		_gate.Release();
	}

	// The connection's async stop drains users of the semaphore. Synchronous disposal only prevents
	// new refreshes; disposing the semaphore itself can race an in-flight finally/release.
	public void Dispose() => Interlocked.Exchange(ref _stopping, 1);

	private async Task<string> RefreshAsync(TwitchTokens observed, CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _stopping) != 0, this);
		await _gate.WaitAsync(cancellationToken);
		try
		{
			ObjectDisposedException.ThrowIf(Volatile.Read(ref _stopping) != 0, this);
			if (NeedsReauthorization)
			{
				throw new TwitchOAuthRejectedException("This Twitch account has to be connected again.");
			}

			var current = _tokens;
			if (!ReferenceEquals(current, observed))
			{
				return current.AccessToken;
			}

			try
			{
				var refreshed = await _client.RefreshAsync(_clientId, current.RefreshToken, cancellationToken);
				_tokens = refreshed;
				_persister.Enqueue(_entryId, refreshed);
				_logger.Debug("Refreshed the Twitch token for entry {EntryId}", _entryId);
				return refreshed.AccessToken;
			}
			catch (TwitchOAuthRejectedException)
			{
				NeedsReauthorization = true;
				_logger.Warning("Twitch rejected the stored credentials for entry {EntryId}", _entryId);
				throw;
			}
		}
		finally
		{
			_gate.Release();
		}
	}

	private async Task AdoptScopesAsync(IReadOnlyList<string> scopes, CancellationToken cancellationToken)
	{
		if (scopes.Count == 0 || scopes.SequenceEqual(_tokens.Scopes, StringComparer.Ordinal))
		{
			return;
		}

		// Under the gate so it cannot clobber a refresh that is landing at the same moment.
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _stopping) != 0, this);
		await _gate.WaitAsync(cancellationToken);
		try
		{
			ObjectDisposedException.ThrowIf(Volatile.Read(ref _stopping) != 0, this);
			_tokens = _tokens with { Scopes = scopes };
		}
		finally
		{
			_gate.Release();
		}
	}

	private static bool IsExpiring(TwitchTokens tokens)
		=> tokens.ExpiresAt - DateTimeOffset.UtcNow <= _refreshMargin;
}
