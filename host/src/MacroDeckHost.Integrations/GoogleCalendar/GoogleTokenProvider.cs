using Serilog;

namespace MacroDeckHost.Integrations.GoogleCalendar;

internal sealed record GoogleTokens(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt);

internal sealed class GoogleTokenProvider : IDisposable
{
	private static readonly TimeSpan _refreshMargin = TimeSpan.FromMinutes(5);

	private readonly Guid _entryId;
	private readonly string _clientId;
	private readonly string _clientSecret;
	private readonly IGoogleOAuthClient _client;
	private readonly Func<GoogleTokens, bool, Task> _persist;
	private readonly TimeProvider _time;
	private readonly ILogger _logger;
	private readonly SemaphoreSlim _gate = new(1, 1);

	private volatile GoogleTokens _tokens;
	private volatile bool _needsReauthorization;
	private int _stopping;

	public GoogleTokenProvider(
		Guid entryId,
		string clientId,
		string clientSecret,
		GoogleTokens tokens,
		IGoogleOAuthClient client,
		Func<GoogleTokens, bool, Task> persist,
		TimeProvider time,
		ILogger logger)
	{
		_entryId = entryId;
		_clientId = clientId;
		_clientSecret = clientSecret;
		_tokens = tokens;
		_client = client;
		_persist = persist;
		_time = time;
		_logger = logger;
	}

	public bool NeedsReauthorization => _needsReauthorization;

	public async Task<string> GetAsync(CancellationToken cancellationToken)
	{
		var observed = _tokens;
		return observed.ExpiresAt - _time.GetUtcNow() <= _refreshMargin
			? await RefreshAsync(observed, cancellationToken)
			: observed.AccessToken;
	}

	public Task<string> ForceRefreshAsync(string rejectedAccessToken, CancellationToken cancellationToken)
	{
		var observed = _tokens;
		return observed.AccessToken == rejectedAccessToken
			? RefreshAsync(observed, cancellationToken)
			: Task.FromResult(observed.AccessToken);
	}

	public async Task StopAsync()
	{
		Interlocked.Exchange(ref _stopping, 1);
		await _gate.WaitAsync();
		_gate.Release();
	}

	// Only blocks new refreshes. Disposing the semaphore could race a refresh still releasing it.
	public void Dispose() => Interlocked.Exchange(ref _stopping, 1);

	private async Task<string> RefreshAsync(GoogleTokens observed, CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _stopping) != 0, this);
		await _gate.WaitAsync(cancellationToken);
		try
		{
			ObjectDisposedException.ThrowIf(Volatile.Read(ref _stopping) != 0, this);
			if (_needsReauthorization)
			{
				throw new GoogleOAuthRejectedException("This Google account has to be connected again.");
			}

			var current = _tokens;
			if (!ReferenceEquals(current, observed))
			{
				return current.AccessToken;
			}

			GoogleTokenResponse response;
			try
			{
				response = await _client.RefreshAsync(_clientId, _clientSecret, current.RefreshToken, cancellationToken);
			}
			catch (GoogleOAuthRejectedException ex)
			{
				_needsReauthorization = true;
				_logger.Warning("Google rejected the stored credentials for entry {EntryId}: {Reason}",
					_entryId,
					ex.Message);
				throw;
			}

			var rotated = !string.IsNullOrEmpty(response.RefreshToken) &&
				!string.Equals(response.RefreshToken, current.RefreshToken, StringComparison.Ordinal);
			var refreshed = new GoogleTokens(response.AccessToken,
				rotated ? response.RefreshToken! : current.RefreshToken,
				_time.GetUtcNow() + response.ExpiresIn);
			_tokens = refreshed;

			try
			{
				await _persist(refreshed, rotated);
			}
			catch (Exception ex)
			{
				_logger.Error(ex, "Failed to persist the refreshed Google token for entry {EntryId}", _entryId);
			}

			return refreshed.AccessToken;
		}
		finally
		{
			_gate.Release();
		}
	}
}
