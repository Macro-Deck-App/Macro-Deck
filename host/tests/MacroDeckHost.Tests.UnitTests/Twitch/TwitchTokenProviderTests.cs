using MacroDeckHost.Integrations.Twitch;
using MacroDeckHost.Integrations.Twitch.Auth;
using Serilog;
using Serilog.Core;

namespace MacroDeckHost.Tests.UnitTests.Twitch;

[TestFixture]
internal sealed class TwitchTokenProviderTests
{
	private static readonly Guid _entryId = Guid.Parse("22222222-2222-2222-2222-222222222222");
	private static readonly string[] _firstRefreshToken = ["refresh-0"];

	private RecordingIntegrationConfig _config = null!;
	private TwitchTokenPersister _persister = null!;

	[SetUp]
	public void SetUp()
	{
		_config = new RecordingIntegrationConfig();
		_persister = new TwitchTokenPersister(_config, SilentLogger());
	}

	[TearDown]
	public void TearDown()
	{
		_persister.Dispose();
	}

	[Test]
	public async Task A_token_that_is_not_close_to_expiring_is_returned_without_a_network_call()
	{
		var client = new FakeTwitchOAuthClient();
		using var provider = Provider(client, FakeTwitchOAuthClient.Tokens("access", "refresh", TimeSpan.FromHours(4)));

		var token = await provider.GetAsync(CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(token, Is.EqualTo("access"));
			Assert.That(client.Calls, Is.Empty);
		});
	}

	[Test]
	public async Task A_token_inside_the_refresh_margin_is_refreshed_first()
	{
		var client = new FakeTwitchOAuthClient();
		using var provider =
			Provider(client, FakeTwitchOAuthClient.Tokens("stale", "refresh-0", TimeSpan.FromMinutes(1)));

		var token = await provider.GetAsync(CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(token, Is.EqualTo("access-1"));
			Assert.That(client.UsedRefreshTokens, Is.EqualTo(_firstRefreshToken));
		});
	}

	[Test]
	public async Task Two_callers_at_expiry_trigger_exactly_one_refresh()
	{
		var bothArrived = new TaskCompletionSource();
		var release = new TaskCompletionSource();
		var client = new FakeTwitchOAuthClient
		{
			BeforeRefresh = async () =>
			{
				bothArrived.TrySetResult();
				await release.Task;
			}
		};
		using var provider =
			Provider(client, FakeTwitchOAuthClient.Tokens("stale", "refresh-0", TimeSpan.FromMinutes(1)));

		var first = provider.GetAsync(CancellationToken.None);
		await bothArrived.Task.WaitAsync(TimeSpan.FromSeconds(5));
		var second = provider.GetAsync(CancellationToken.None);

		release.SetResult();
		var tokens = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));

		Assert.Multiple(() =>
		{
			Assert.That(client.RefreshCount, Is.EqualTo(1), "the rotating refresh token must be spent once");
			Assert.That(tokens[0], Is.EqualTo("access-1"));
			Assert.That(tokens[1], Is.EqualTo("access-1"), "the queued caller has to take the fresh token");
		});
	}

	[Test]
	public async Task StopAsync_drains_an_in_flight_refresh_before_the_persistence_queue_closes()
	{
		var refreshStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var releaseRefresh = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var client = new FakeTwitchOAuthClient
		{
			BeforeRefresh = async () =>
			{
				refreshStarted.TrySetResult();
				await releaseRefresh.Task.WaitAsync(TimeSpan.FromSeconds(5));
			}
		};
		using var provider =
			Provider(client, FakeTwitchOAuthClient.Tokens("stale", "refresh-0", TimeSpan.FromMinutes(1)));

		var refreshing = provider.GetAsync(CancellationToken.None);
		await refreshStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
		var stopping = provider.StopAsync();

		Assert.That(stopping.IsCompleted, Is.False);
		Assert.That(async () => await provider.ForceRefreshAsync(CancellationToken.None),
			Throws.InstanceOf<ObjectDisposedException>());

		releaseRefresh.SetResult();
		await Task.WhenAll(refreshing, stopping).WaitAsync(TimeSpan.FromSeconds(5));
		await _persister.CompleteAsync().WaitAsync(TimeSpan.FromSeconds(5));

		Assert.That(_config.Secrets[(_entryId, TwitchConfigKeys.RefreshToken)], Is.EqualTo("refresh-1"));
	}

	[Test]
	public async Task A_refresh_is_persisted_with_the_refresh_token_first()
	{
		var client = new FakeTwitchOAuthClient();
		using var provider =
			Provider(client, FakeTwitchOAuthClient.Tokens("stale", "refresh-0", TimeSpan.FromMinutes(1)));

		await provider.GetAsync(CancellationToken.None);
		await _persister.FlushAsync();

		Assert.Multiple(() =>
		{
			Assert.That(_config.Secrets[(_entryId, TwitchConfigKeys.RefreshToken)], Is.EqualTo("refresh-1"));
			Assert.That(_config.Secrets[(_entryId, TwitchConfigKeys.AccessToken)], Is.EqualTo("access-1"));
			Assert.That(_config.WriteOrder[0], Is.EqualTo(TwitchConfigKeys.RefreshToken));
		});
	}

	[Test]
	public async Task CompleteAsync_drains_all_Twitch_rotations_in_fifo_order_and_rejects_late_writes()
	{
		var firstWriteStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var releaseFirstWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var writes = 0;
		var config = new RecordingIntegrationConfig
		{
			BeforeSecretWrite = async _ =>
			{
				if (Interlocked.Increment(ref writes) == 1)
				{
					firstWriteStarted.TrySetResult();
					await releaseFirstWrite.Task.WaitAsync(TimeSpan.FromSeconds(5));
				}
			}
		};
		using var persister = new TwitchTokenPersister(config, SilentLogger());
		var first = new TwitchTokens("access-1", "refresh-1", DateTimeOffset.UtcNow.AddHours(1), []);
		var second = new TwitchTokens("access-2", "refresh-2", DateTimeOffset.UtcNow.AddHours(2), []);

		Assert.That(persister.Enqueue(_entryId, first), Is.True);
		await firstWriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
		Assert.That(persister.Enqueue(_entryId, second), Is.True);

		var completing = persister.CompleteAsync();
		Assert.That(persister.Enqueue(_entryId,
				new TwitchTokens("late-access", "late-refresh", DateTimeOffset.UtcNow, [])),
			Is.False);
		Assert.That(completing.IsCompleted, Is.False);

		releaseFirstWrite.SetResult();
		await completing.WaitAsync(TimeSpan.FromSeconds(5));

		Assert.Multiple(() =>
		{
			Assert.That(config.Secrets[(_entryId, TwitchConfigKeys.RefreshToken)], Is.EqualTo("refresh-2"));
			Assert.That(config.Secrets[(_entryId, TwitchConfigKeys.AccessToken)], Is.EqualTo("access-2"));
			Assert.That(config.WriteOrder.Take(4),
				Is.EqualTo(new[]
				{
					TwitchConfigKeys.RefreshToken,
					TwitchConfigKeys.AccessToken,
					TwitchConfigKeys.ExpiresAt,
					TwitchConfigKeys.RefreshToken
				}));
		});
	}

	[Test]
	public void A_rejected_refresh_token_needs_a_new_authorization()
	{
		var client = new FakeTwitchOAuthClient();
		client.RefreshResults.Enqueue(() => throw new TwitchOAuthRejectedException("Invalid refresh token"));
		using var provider =
			Provider(client, FakeTwitchOAuthClient.Tokens("stale", "refresh-0", TimeSpan.FromMinutes(1)));

		Assert.ThrowsAsync<TwitchOAuthRejectedException>(() => provider.GetAsync(CancellationToken.None));
		Assert.That(provider.NeedsReauthorization, Is.True);
	}

	[Test]
	public void A_transient_refresh_failure_keeps_the_account_alive()
	{
		var client = new FakeTwitchOAuthClient();
		client.RefreshResults.Enqueue(() => throw new TwitchOAuthTransientException("Twitch could not be reached."));
		using var provider =
			Provider(client, FakeTwitchOAuthClient.Tokens("stale", "refresh-0", TimeSpan.FromMinutes(1)));

		Assert.ThrowsAsync<TwitchOAuthTransientException>(() => provider.GetAsync(CancellationToken.None));
		Assert.That(provider.NeedsReauthorization, Is.False);
	}

	[Test]
	public async Task Once_rejected_no_further_refresh_is_attempted()
	{
		var client = new FakeTwitchOAuthClient();
		client.RefreshResults.Enqueue(() => throw new TwitchOAuthRejectedException("Invalid refresh token"));
		using var provider =
			Provider(client, FakeTwitchOAuthClient.Tokens("stale", "refresh-0", TimeSpan.FromMinutes(1)));

		Assert.ThrowsAsync<TwitchOAuthRejectedException>(() => provider.GetAsync(CancellationToken.None));
		Assert.ThrowsAsync<TwitchOAuthRejectedException>(() => provider.GetAsync(CancellationToken.None));

		Assert.That(client.RefreshCount, Is.EqualTo(1));
		await Task.CompletedTask;
	}

	[Test]
	public async Task Validation_adopts_the_scopes_twitch_actually_granted()
	{
		var granted = new[] { TwitchScopes.UserReadChat, TwitchScopes.ChannelManageBroadcast };
		var client = new FakeTwitchOAuthClient
		{
			Identity = new TwitchTokenIdentity("12345", "streamer", "client-id", granted, TimeSpan.FromHours(4))
		};
		using var provider = Provider(client, FakeTwitchOAuthClient.Tokens("access", "refresh", TimeSpan.FromHours(4)));

		var identity = await provider.ValidateAsync(CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(identity.Login, Is.EqualTo("streamer"));
			Assert.That(provider.Scopes, Is.EqualTo(granted));
		});
	}

	[Test]
	public async Task A_rejected_validation_is_retried_once_behind_a_refresh()
	{
		var client = new RetryingOAuthClient();
		using var provider = Provider(client, FakeTwitchOAuthClient.Tokens("dead", "refresh-0", TimeSpan.FromHours(4)));

		var identity = await provider.ValidateAsync(CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(identity.Login, Is.EqualTo("streamer"));
			Assert.That(client.RefreshCount, Is.EqualTo(1));
		});
	}

	private TwitchTokenProvider Provider(ITwitchOAuthClient client, TwitchTokens tokens)
		=> new(_entryId, "client-id", tokens, client, _persister, SilentLogger());

	private static Logger SilentLogger() => new LoggerConfiguration().CreateLogger();

	private sealed class RetryingOAuthClient : ITwitchOAuthClient
	{
		private int _validations;

		public int RefreshCount { get; private set; }

		public Task<TwitchDeviceCode> RequestDeviceCodeAsync(
			string clientId,
			string scopes,
			CancellationToken cancellationToken)
			=> throw new NotSupportedException();

		public Task<TwitchTokenPollResult> PollTokenAsync(
			string clientId,
			string scopes,
			string deviceCode,
			CancellationToken cancellationToken)
			=> throw new NotSupportedException();

		public Task<TwitchTokens> RefreshAsync(
			string clientId,
			string refreshToken,
			CancellationToken cancellationToken)
		{
			RefreshCount++;
			return Task.FromResult(FakeTwitchOAuthClient.Tokens("fresh", "refresh-1", TimeSpan.FromHours(4)));
		}

		public Task<TwitchTokenIdentity> ValidateAsync(string accessToken, CancellationToken cancellationToken)
		{
			_validations++;

			return _validations == 1
				? Task.FromException<TwitchTokenIdentity>(new TwitchOAuthRejectedException("expired"))
				: Task.FromResult(new TwitchTokenIdentity("12345",
					"streamer",
					"client-id",
					TwitchScopes.All,
					TimeSpan.FromHours(4)));
		}

		public void Dispose()
		{
		}
	}
}
