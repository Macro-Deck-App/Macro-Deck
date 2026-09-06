using MacroDeckHost.Integrations.Delegation.Protocol;

namespace MacroDeckHost.Integrations.Delegation;

internal sealed class DelegateSession : IDisposable
{
	private static readonly TimeSpan RenewMargin = TimeSpan.FromSeconds(60);

	private readonly IDelegateClient _client;
	private readonly Uri _baseUrl;
	private readonly string _username;
	private readonly string _password;
	private readonly TimeProvider _time;
	private readonly SemaphoreSlim _lock = new(1, 1);

	private string? _token;
	private DateTimeOffset _expiresAt;

	public DelegateSession(IDelegateClient client, Uri baseUrl, string username, string password, TimeProvider time)
	{
		_client = client;
		_baseUrl = baseUrl;
		_username = username;
		_password = password;
		_time = time;
	}

	public bool CredentialsRejected { get; private set; }

	public DateTimeOffset? ThrottledUntil { get; private set; }

	public async Task<string> GetTokenAsync(CancellationToken cancellationToken)
	{
		if (TryGetCachedToken(out var cached))
		{
			return cached;
		}

		ThrowIfLatched();

		await _lock.WaitAsync(cancellationToken);
		try
		{
			if (TryGetCachedToken(out cached))
			{
				return cached;
			}

			ThrowIfLatched();
			return await LoginAsync(cancellationToken);
		}
		finally
		{
			_lock.Release();
		}
	}

	public async Task<string> RenewAfterUnauthorizedAsync(CancellationToken cancellationToken)
	{
		await _lock.WaitAsync(cancellationToken);
		try
		{
			_token = null;
		}
		finally
		{
			_lock.Release();
		}

		return await GetTokenAsync(cancellationToken);
	}

	private bool TryGetCachedToken(out string token)
	{
		if (_token is { } value && _time.GetUtcNow() < _expiresAt - RenewMargin)
		{
			token = value;
			return true;
		}

		token = string.Empty;
		return false;
	}

	private void ThrowIfLatched()
	{
		if (CredentialsRejected)
		{
			throw new DelegateCredentialsRejectedException();
		}

		var now = _time.GetUtcNow();
		if (ThrottledUntil is { } until && now < until)
		{
			throw new DelegateThrottledException(until - now);
		}
	}

	private async Task<string> LoginAsync(CancellationToken cancellationToken)
	{
		DelegateLoginResult result;
		try
		{
			result = await _client.LoginAsync(_baseUrl, _username, _password, cancellationToken);
		}
		catch (DelegateUnauthorizedException)
		{
			CredentialsRejected = true;
			throw new DelegateCredentialsRejectedException();
		}
		catch (DelegateRateLimitedException ex)
		{
			ThrottledUntil = _time.GetUtcNow() + ex.RetryAfter;
			throw;
		}

		_token = result.AccessToken;
		_expiresAt = _time.GetUtcNow() + result.ExpiresIn;
		ThrottledUntil = null;
		return _token;
	}

	public void Dispose() => _lock.Dispose();
}
