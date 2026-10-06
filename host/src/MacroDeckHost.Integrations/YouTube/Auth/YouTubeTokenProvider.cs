using Serilog;

namespace MacroDeckHost.Integrations.YouTube.Auth;

internal sealed class YouTubeTokenProvider : IDisposable
{
	private static readonly TimeSpan _refreshMargin = TimeSpan.FromMinutes(5);

	private readonly Guid _entryId;
	private readonly string _clientId;
	private readonly string _clientSecret;
	private readonly IYouTubeOAuthClient _client;
	private readonly YouTubeTokenPersister _persister;
	private readonly ILogger _logger;
	private readonly TimeProvider _time;
	private readonly SemaphoreSlim _gate = new(1, 1);
	private int _stopping;

	private volatile YouTubeTokens _tokens;

	public YouTubeTokenProvider(
		Guid entryId,
		string clientId,
		string clientSecret,
		YouTubeTokens tokens,
		IYouTubeOAuthClient client,
		YouTubeTokenPersister persister,
		ILogger logger,
		TimeProvider? timeProvider = null)
	{
		_entryId = entryId;
		_clientId = clientId;
		_clientSecret = clientSecret;
		_tokens = tokens;
		_client = client;
		_persister = persister;
		_logger = logger;
		_time = timeProvider ?? TimeProvider.System;
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

	public async Task StopAsync()
	{
		Interlocked.Exchange(ref _stopping, 1);
		await _gate.WaitAsync();
		_gate.Release();
	}

	// The connection's async stop drains users of the semaphore. Synchronous disposal only prevents
	// new refreshes; disposing the semaphore itself can race an in-flight finally/release.
	public void Dispose() => Interlocked.Exchange(ref _stopping, 1);

	private async Task<string> RefreshAsync(YouTubeTokens observed, CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _stopping) != 0, this);
		await _gate.WaitAsync(cancellationToken);
		try
		{
			ObjectDisposedException.ThrowIf(Volatile.Read(ref _stopping) != 0, this);
			if (NeedsReauthorization)
			{
				throw new YouTubeOAuthRejectedException("This YouTube account has to be connected again.");
			}

			var current = _tokens;
			if (!ReferenceEquals(current, observed))
			{
				return current.AccessToken;
			}

			try
			{
				var refreshed = await _client.RefreshAsync(_clientId,
					_clientSecret,
					current.RefreshToken,
					cancellationToken);

				if (refreshed.Scopes.Count == 0)
				{
					refreshed = refreshed with { Scopes = current.Scopes };
				}

				_tokens = refreshed;
				_persister.Enqueue(_entryId, refreshed);
				_logger.Debug("Refreshed the YouTube token for entry {EntryId}", _entryId);
				return refreshed.AccessToken;
			}
			catch (YouTubeOAuthRejectedException ex)
			{
				NeedsReauthorization = true;
				_logger.Warning("Google rejected the stored YouTube credentials for entry {EntryId} ({ErrorCode})",
					_entryId,
					ex.ErrorCode);
				throw;
			}
		}
		finally
		{
			_gate.Release();
		}
	}

	private bool IsExpiring(YouTubeTokens tokens)
		=> tokens.ExpiresAt - _time.GetUtcNow() <= _refreshMargin;
}
