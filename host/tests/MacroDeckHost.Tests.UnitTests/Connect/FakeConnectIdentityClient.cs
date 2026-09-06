using MacroDeckHost.Application.Connect;

namespace MacroDeckHost.Tests.UnitTests.Connect;

internal sealed class FakeConnectIdentityClient : IConnectIdentityClient
{
	private static readonly TimeSpan _refreshTokenLifetime = TimeSpan.FromDays(180);
	private static readonly TimeSpan _accessTokenLifetime = TimeSpan.FromMinutes(10);

	private readonly Lock _sync = new();
	private readonly TimeProvider _time;
	private readonly Dictionary<string, Issued> _issued = new(StringComparer.Ordinal);

	private bool _chainDead;
	private int _refreshes;
	private int _deviceAuthorizations;
	private int _polls;
	private int _revokes;
	private int _minted;

	public FakeConnectIdentityClient(TimeProvider time) => _time = time;

	public string Subject { get; set; } = "connect-subject";

	public string DisplayName { get; set; } = "Ada Lovelace";

	public string? Picture { get; set; }

	public string? CreatorUsername { get; set; }

	public IReadOnlyList<string> Roles { get; set; } = [];

	public Queue<Func<ConnectTokenResponse>> Results { get; } = new();

	public Func<Task>? BeforeRefresh { get; set; }

	public bool Stall { get; set; }

	public TaskCompletionSource StallGate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

	public List<string> IssuedRefreshTokens { get; } = [];

	public List<string> PresentedRefreshTokens { get; } = [];

	public List<string> RevokedTokens { get; } = [];

	public List<string> PolledDeviceCodes { get; } = [];

	/// <summary>Answers for successive polls. An empty queue keeps answering <c>Pending</c>, which is what
	/// a user who has not confirmed yet looks like.</summary>
	public Queue<ConnectDevicePollResult> PollResults { get; } = new();

	public ConnectDeviceAuthorization DeviceAuthorization { get; set; } = new("device-code-1",
		"1234-5678",
		new Uri("https://accounts.macro-deck.app/device"),
		new Uri("https://accounts.macro-deck.app/device?user_code=1234-5678"),
		TimeSpan.FromMinutes(15),
		TimeSpan.FromSeconds(5));

	public Exception? DeviceAuthorizationFailure { get; set; }

	/// <summary>When set, every poll throws it - a network failure rather than a protocol answer.</summary>
	public Exception? PollFailure { get; set; }

	public int RefreshCount => Volatile.Read(ref _refreshes);

	public int DeviceAuthorizationCount => Volatile.Read(ref _deviceAuthorizations);

	public int PollCount => Volatile.Read(ref _polls);

	public int RevokeCount => Volatile.Read(ref _revokes);

	public string? CurrentRefreshToken
	{
		get
		{
			lock (_sync)
			{
				return IssuedRefreshTokens.Count == 0 ? null : IssuedRefreshTokens[^1];
			}
		}
	}

	public bool ChainIsAlive
	{
		get
		{
			lock (_sync)
			{
				return !_chainDead;
			}
		}
	}

	public Task<ConnectDeviceAuthorization> RequestDeviceAuthorization(CancellationToken cancellationToken)
	{
		Interlocked.Increment(ref _deviceAuthorizations);

		return DeviceAuthorizationFailure is { } failure
			? Task.FromException<ConnectDeviceAuthorization>(failure)
			: Task.FromResult(DeviceAuthorization);
	}

	public async Task<ConnectDevicePollResult> PollDeviceToken(
		string deviceCode,
		CancellationToken cancellationToken)
	{
		Interlocked.Increment(ref _polls);
		lock (_sync)
		{
			PolledDeviceCodes.Add(deviceCode);
		}

		await Gate(cancellationToken);

		if (PollFailure is { } failure)
		{
			throw failure;
		}

		ConnectDevicePollResult? scripted;
		lock (_sync)
		{
			scripted = PollResults.Count > 0 ? PollResults.Dequeue() : null;
		}

		if (scripted is null)
		{
			return new ConnectDevicePollResult(ConnectDevicePollStatus.Pending);
		}

		if (scripted.Status is not ConnectDevicePollStatus.Success)
		{
			return scripted;
		}

		lock (_sync)
		{
			_chainDead = false;
			return scripted with { Tokens = scripted.Tokens ?? Mint() };
		}
	}

	public async Task<ConnectTokenResponse> Refresh(string refreshToken, CancellationToken cancellationToken)
	{
		Interlocked.Increment(ref _refreshes);
		lock (_sync)
		{
			PresentedRefreshTokens.Add(refreshToken);
		}

		if (BeforeRefresh is { } hook)
		{
			await hook();
		}

		await Gate(cancellationToken);

		// A scripted outcome is applied before the token is validated: a transport failure must never
		// redeem or invalidate the token the attempt presented.
		if (Dequeue() is { } scripted)
		{
			return scripted();
		}

		lock (_sync)
		{
			if (_chainDead)
			{
				throw new ConnectAuthRejectedException("invalid_grant: the authorization was revoked.");
			}

			if (!_issued.TryGetValue(refreshToken, out var state))
			{
				throw new ConnectAuthRejectedException("invalid_grant: unknown refresh token.");
			}

			if (state.Redeemed)
			{
				// Replaying a redeemed refresh token revokes the whole authorization on the real server.
				_chainDead = true;
				throw new ConnectAuthRejectedException("invalid_grant: the refresh token was already redeemed.");
			}

			if (_time.GetUtcNow() > state.ExpiresAt)
			{
				throw new ConnectAuthRejectedException("invalid_grant: the refresh token expired.");
			}

			state.Redeemed = true;
			return Mint();
		}
	}

	public Task Revoke(string refreshToken, CancellationToken cancellationToken)
	{
		Interlocked.Increment(ref _revokes);
		lock (_sync)
		{
			RevokedTokens.Add(refreshToken);
			_chainDead = true;
		}

		if (Dequeue() is { } scripted)
		{
			scripted();
		}

		return Task.CompletedTask;
	}

	public static Func<ConnectTokenResponse> Unreachable()
		=> () => throw new ConnectAuthTransientException("Macro Deck Connect could not be reached.",
			new HttpRequestException("connection refused"));

	public static Func<ConnectTokenResponse> ServerError(int status)
		=> () => throw new ConnectAuthTransientException($"Macro Deck Connect answered {status}.");

	public static Func<ConnectTokenResponse> MalformedBody()
		=> () => throw new ConnectAuthTransientException("Macro Deck Connect returned an unreadable token response.");

	public static Func<ConnectTokenResponse> InvalidGrant()
		=> () => throw new ConnectAuthRejectedException("Macro Deck Connect rejected the credential: invalid_grant.");

	public static Func<ConnectTokenResponse> Suspended()
		=> () => throw new ConnectAccountSuspendedException("This account has been suspended.");

	public static Func<ConnectTokenResponse> RateLimited(TimeSpan retryAfter)
		=> () => throw new ConnectAuthTransientException("Macro Deck Connect is rate-limiting the token endpoint.")
		{
			IsRateLimit = true, RetryAfter = retryAfter
		};

	public ConnectCredential SeedCredential(DateTimeOffset issuedAt)
	{
		lock (_sync)
		{
			var token = $"refresh-{++_minted}";
			_issued[token] = new Issued(issuedAt + _refreshTokenLifetime);
			IssuedRefreshTokens.Add(token);

			return new ConnectCredential(token, Subject, DisplayName, Picture, issuedAt);
		}
	}

	private async Task Gate(CancellationToken cancellationToken)
	{
		if (Stall)
		{
			await StallGate.Task.WaitAsync(cancellationToken);
		}
	}

	private Func<ConnectTokenResponse>? Dequeue()
	{
		lock (_sync)
		{
			return Results.Count > 0 ? Results.Dequeue() : null;
		}
	}

	private ConnectTokenResponse Mint()
	{
		var now = _time.GetUtcNow();
		var token = $"refresh-{++_minted}";
		_issued[token] = new Issued(now + _refreshTokenLifetime);
		IssuedRefreshTokens.Add(token);

		return new ConnectTokenResponse($"access-{_minted}",
			token,
			ConnectJwt.Create(Subject,
				DisplayName,
				Picture,
				CreatorUsername,
				Roles,
				null,
				now + TimeSpan.FromHours(1)),
			_accessTokenLifetime);
	}

	private sealed class Issued
	{
		public Issued(DateTimeOffset expiresAt) => ExpiresAt = expiresAt;

		public DateTimeOffset ExpiresAt { get; }

		public bool Redeemed { get; set; }
	}
}
