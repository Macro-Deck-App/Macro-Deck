using Serilog;

namespace MacroDeckHost.Integrations.MicrosoftCalendar;

internal sealed record MicrosoftTokens(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt);

internal sealed class MicrosoftTokenProvider : IDisposable
{
	private static readonly TimeSpan _refreshMargin = TimeSpan.FromMinutes(5);

	private readonly Guid _entryId;
	private readonly string _clientId;
	private readonly string _tenant;
	private readonly IMicrosoftOAuthClient _client;
	private readonly Func<MicrosoftTokens, bool, Task> _persist;
	private readonly TimeProvider _time;
	private readonly ILogger _logger;
	private readonly SemaphoreSlim _gate = new(1, 1);

	private volatile MicrosoftTokens _tokens;
	private volatile bool _needsReauthorization;
	private int _stopping;

	public MicrosoftTokenProvider(
		Guid entryId,
		string clientId,
		string tenant,
		MicrosoftTokens tokens,
		IMicrosoftOAuthClient client,
		Func<MicrosoftTokens, bool, Task> persist,
		TimeProvider time,
		ILogger logger)
	{
		_entryId = entryId;
		_clientId = clientId;
		_tenant = tenant;
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

	private async Task<string> RefreshAsync(MicrosoftTokens observed, CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _stopping) != 0, this);
		await _gate.WaitAsync(cancellationToken);
		try
		{
			ObjectDisposedException.ThrowIf(Volatile.Read(ref _stopping) != 0, this);
			if (_needsReauthorization)
			{
				throw new MicrosoftOAuthRejectedException("This Microsoft account has to be connected again.");
			}

			var current = _tokens;
			if (!ReferenceEquals(current, observed))
			{
				return current.AccessToken;
			}

			MicrosoftTokenResponse response;
			try
			{
				response = await _client.RefreshAsync(_clientId, _tenant, current.RefreshToken, cancellationToken);
			}
			catch (MicrosoftOAuthRejectedException ex)
			{
				_needsReauthorization = true;
				_logger.Warning("Microsoft rejected the stored credentials for entry {EntryId}: {Reason}",
					_entryId,
					ex.Message);
				throw;
			}

			var rotated = !string.IsNullOrEmpty(response.RefreshToken) &&
				!string.Equals(response.RefreshToken, current.RefreshToken, StringComparison.Ordinal);
			var refreshed = new MicrosoftTokens(response.AccessToken,
				rotated ? response.RefreshToken! : current.RefreshToken,
				_time.GetUtcNow() + response.ExpiresIn);
			_tokens = refreshed;

			try
			{
				await _persist(refreshed, rotated);
			}
			catch (Exception ex)
			{
				_logger.Error(ex, "Failed to persist the refreshed Microsoft token for entry {EntryId}", _entryId);
			}

			return refreshed.AccessToken;
		}
		finally
		{
			_gate.Release();
		}
	}
}
