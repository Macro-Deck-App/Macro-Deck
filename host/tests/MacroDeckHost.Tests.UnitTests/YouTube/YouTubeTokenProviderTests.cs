using MacroDeck.Sdk.ConfigFlow;
using MacroDeckHost.Integrations.YouTube;
using MacroDeckHost.Integrations.YouTube.Auth;
using Serilog;
using Serilog.Core;

namespace MacroDeckHost.Tests.UnitTests.YouTube;

[TestFixture]
internal sealed class YouTubeTokenProviderTests
{
	private static readonly Guid _entryId = Guid.Parse("33333333-3333-3333-3333-333333333333");

	private RecordingConfig _config = null!;
	private YouTubeTokenPersister _persister = null!;

	[SetUp]
	public void SetUp()
	{
		_config = new RecordingConfig();
		_persister = new YouTubeTokenPersister(_config, SilentLogger());
	}

	[TearDown]
	public void TearDown()
	{
		_persister.Dispose();
	}

	[Test]
	public async Task A_token_that_is_not_close_to_expiring_is_returned_without_a_refresh()
	{
		var client = new FakeOAuthClient();
		using var provider = Provider(client, Tokens("access", "refresh", TimeSpan.FromHours(1)));

		var token = await provider.GetAsync(CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(token, Is.EqualTo("access"));
			Assert.That(client.RefreshCount, Is.Zero);
		});
	}

	[Test]
	public async Task A_token_inside_the_five_minute_margin_is_refreshed_with_the_client_secret()
	{
		var client = new FakeOAuthClient();
		using var provider = Provider(client, Tokens("stale", "refresh-0", TimeSpan.FromMinutes(4)));

		var token = await provider.GetAsync(CancellationToken.None);
		await _persister.FlushAsync();

		Assert.Multiple(() =>
		{
			Assert.That(token, Is.EqualTo("access-1"));
			Assert.That(client.UsedSecrets, Is.EqualTo(new[] { "secret" }));
			Assert.That(client.UsedRefreshTokens, Is.EqualTo(new[] { "refresh-0" }));
			Assert.That(_config.Secrets[(_entryId, YouTubeConfigKeys.AccessToken)], Is.EqualTo("access-1"));
			Assert.That(_config.Secrets[(_entryId, YouTubeConfigKeys.RefreshToken)], Is.EqualTo("refresh-1"));
			Assert.That(_config.Strings.ContainsKey((_entryId, YouTubeConfigKeys.ExpiresAt)), Is.True);
		});
	}

	[Test]
	public async Task Two_callers_at_expiry_trigger_exactly_one_refresh()
	{
		var bothArrived = new TaskCompletionSource();
		var release = new TaskCompletionSource();
		var client = new FakeOAuthClient
		{
			BeforeRefresh = async () =>
			{
				bothArrived.TrySetResult();
				await release.Task;
			}
		};
		using var provider = Provider(client, Tokens("stale", "refresh-0", TimeSpan.FromMinutes(1)));

		var first = provider.GetAsync(CancellationToken.None);
		await bothArrived.Task.WaitAsync(TimeSpan.FromSeconds(5));
		var second = provider.GetAsync(CancellationToken.None);

		release.SetResult();
		var tokens = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));

		Assert.Multiple(() =>
		{
			Assert.That(client.RefreshCount, Is.EqualTo(1));
			Assert.That(tokens, Is.All.EqualTo("access-1"));
		});
	}

	[Test]
	public async Task A_forced_refresh_replaces_a_token_google_no_longer_accepts()
	{
		var client = new FakeOAuthClient();
		using var provider = Provider(client, Tokens("revoked", "refresh-0", TimeSpan.FromHours(1)));

		var token = await provider.ForceRefreshAsync(CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(token, Is.EqualTo("access-1"));
			Assert.That(client.RefreshCount, Is.EqualTo(1));
		});
	}

	[Test]
	public void A_rejected_refresh_token_needs_a_new_authorization_and_is_not_retried()
	{
		var client = new FakeOAuthClient();
		client.RefreshResults.Enqueue(() => throw new YouTubeOAuthRejectedException("dead", "invalid_grant"));
		using var provider = Provider(client, Tokens("stale", "refresh-0", TimeSpan.FromMinutes(1)));

		Assert.ThrowsAsync<YouTubeOAuthRejectedException>(() => provider.GetAsync(CancellationToken.None));
		Assert.ThrowsAsync<YouTubeOAuthRejectedException>(() => provider.GetAsync(CancellationToken.None));

		Assert.Multiple(() =>
		{
			Assert.That(provider.NeedsReauthorization, Is.True);
			Assert.That(client.RefreshCount, Is.EqualTo(1));
		});
	}

	[Test]
	public void A_transient_refresh_failure_keeps_the_account_alive()
	{
		var client = new FakeOAuthClient();
		client.RefreshResults.Enqueue(() => throw new YouTubeOAuthTransientException("unreachable"));
		using var provider = Provider(client, Tokens("stale", "refresh-0", TimeSpan.FromMinutes(1)));

		Assert.ThrowsAsync<YouTubeOAuthTransientException>(() => provider.GetAsync(CancellationToken.None));
		Assert.That(provider.NeedsReauthorization, Is.False);
	}

	[Test]
	public async Task CompleteAsync_drains_queued_rotations_in_order_and_refuses_late_ones()
	{
		var firstWriteStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var releaseFirstWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var writes = 0;
		var config = new RecordingConfig
		{
			BeforeSecretWrite = async () =>
			{
				if (Interlocked.Increment(ref writes) == 1)
				{
					firstWriteStarted.TrySetResult();
					await releaseFirstWrite.Task.WaitAsync(TimeSpan.FromSeconds(5));
				}
			}
		};
		using var persister = new YouTubeTokenPersister(config, SilentLogger());

		Assert.That(persister.Enqueue(_entryId, Tokens("access-1", "refresh-1", TimeSpan.FromHours(1))), Is.True);
		await firstWriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
		Assert.That(persister.Enqueue(_entryId, Tokens("access-2", "refresh-2", TimeSpan.FromHours(1))), Is.True);

		var completing = persister.CompleteAsync();
		Assert.That(persister.Enqueue(_entryId, Tokens("late", "late", TimeSpan.FromHours(1))), Is.False);

		releaseFirstWrite.SetResult();
		await completing.WaitAsync(TimeSpan.FromSeconds(5));

		Assert.Multiple(() =>
		{
			Assert.That(config.Secrets[(_entryId, YouTubeConfigKeys.RefreshToken)], Is.EqualTo("refresh-2"));
			Assert.That(config.Secrets[(_entryId, YouTubeConfigKeys.AccessToken)], Is.EqualTo("access-2"));
		});
	}

	private YouTubeTokenProvider Provider(IYouTubeOAuthClient client, YouTubeTokens tokens)
		=> new(_entryId, "client-id", "secret", tokens, client, _persister, SilentLogger());

	private static YouTubeTokens Tokens(string access, string refresh, TimeSpan validFor)
		=> new(access, refresh, DateTimeOffset.UtcNow + validFor, ["https://www.googleapis.com/auth/youtube"]);

	private static Logger SilentLogger() => new LoggerConfiguration().CreateLogger();

	private sealed class FakeOAuthClient : IYouTubeOAuthClient
	{
		private readonly Lock _lock = new();

		public Queue<Func<YouTubeTokens>> RefreshResults { get; } = new();

		public Func<Task>? BeforeRefresh { get; init; }

		public List<string> UsedSecrets { get; } = [];

		public List<string> UsedRefreshTokens { get; } = [];

		public int RefreshCount { get; private set; }

		public Task<YouTubeDeviceCode> RequestDeviceCodeAsync(string clientId, CancellationToken cancellationToken)
			=> throw new NotSupportedException();

		public Task<YouTubeTokenPoll> PollTokenAsync(
			string clientId,
			string clientSecret,
			string deviceCode,
			CancellationToken cancellationToken)
			=> throw new NotSupportedException();

		public async Task<YouTubeTokens> RefreshAsync(
			string clientId,
			string clientSecret,
			string refreshToken,
			CancellationToken cancellationToken)
		{
			Func<YouTubeTokens> next;
			lock (_lock)
			{
				RefreshCount++;
				var attempt = RefreshCount;
				UsedSecrets.Add(clientSecret);
				UsedRefreshTokens.Add(refreshToken);
				next = RefreshResults.Count > 0
					? RefreshResults.Dequeue()
					: () => Tokens($"access-{attempt}", $"refresh-{attempt}", TimeSpan.FromHours(1));
			}

			if (BeforeRefresh is not null)
			{
				await BeforeRefresh();
			}

			return next();
		}

		public void Dispose()
		{
		}
	}

	private sealed class RecordingConfig : IIntegrationConfig
	{
		private readonly Lock _lock = new();

		public Dictionary<(Guid EntryId, string Key), string?> Strings { get; } = [];

		public Dictionary<(Guid EntryId, string Key), string> Secrets { get; } = [];

		public Func<Task>? BeforeSecretWrite { get; init; }

		public Task<IReadOnlyList<ConfigEntrySnapshot>> GetEntriesAsync(CancellationToken cancellationToken = default)
			=> Task.FromResult<IReadOnlyList<ConfigEntrySnapshot>>([]);

		public Task<string?> GetStringAsync(Guid entryId, string key, CancellationToken cancellationToken = default)
		{
			lock (_lock)
			{
				return Task.FromResult(Strings.GetValueOrDefault((entryId, key)));
			}
		}

		public Task<string?> GetSecretAsync(Guid entryId, string key, CancellationToken cancellationToken = default)
		{
			lock (_lock)
			{
				return Task.FromResult<string?>(Secrets.GetValueOrDefault((entryId, key)));
			}
		}

		public Task SetStringAsync(Guid entryId,
			string key,
			string? value,
			CancellationToken cancellationToken = default)
		{
			lock (_lock)
			{
				Strings[(entryId, key)] = value;
			}

			return Task.CompletedTask;
		}

		public async Task SetSecretAsync(Guid entryId,
			string key,
			string value,
			CancellationToken cancellationToken = default)
		{
			if (BeforeSecretWrite is not null)
			{
				await BeforeSecretWrite();
			}

			lock (_lock)
			{
				Secrets[(entryId, key)] = value;
			}
		}
	}
}
