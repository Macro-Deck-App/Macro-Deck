using MacroDeckHost.Integrations.Spotify;
using MacroDeckHost.Tests.UnitTests.Auth;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace MacroDeckHost.Tests.UnitTests.Spotify;

[TestFixture]
internal sealed class SpotifyTokenManagerTests
{
	private static readonly Guid _entryId = Guid.Parse("2f1c3b4a-5d6e-4f70-8192-a3b4c5d6e7f8");

	private RecordingIntegrationConfig _config = null!;
	private SpotifyTokenPersister _persister = null!;

	[SetUp]
	public void SetUp()
	{
		_config = new RecordingIntegrationConfig();
		_persister = new SpotifyTokenPersister(_config, SilentLogger());
	}

	[TearDown]
	public void TearDown() => _persister.Dispose();

	[Test]
	public async Task A_token_that_is_not_close_to_expiring_needs_no_network_call()
	{
		var oauth = new FakeSpotifyOAuthClient();
		using var manager = Manager(oauth, TimeSpan.FromHours(1));

		await manager.EnsureValidTokenAsync(CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(oauth.RefreshCount, Is.EqualTo(0));
			Assert.That(manager.AccessToken, Is.EqualTo("stored-access"));
			Assert.That(manager.State, Is.EqualTo(SpotifyAuthenticationState.Valid));
		});
	}

	[Test]
	public async Task A_token_inside_the_refresh_margin_is_refreshed_before_the_call_proceeds()
	{
		var oauth = new FakeSpotifyOAuthClient();
		using var manager = Manager(oauth, TimeSpan.FromMinutes(1));

		await manager.EnsureValidTokenAsync(CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(oauth.RefreshCount, Is.EqualTo(1));
			Assert.That(manager.AccessToken, Is.EqualTo("access-1"));
			Assert.That(oauth.UsedRefreshTokens, Has.Count.EqualTo(1).And.All.EqualTo("stored-refresh"));
		});
	}

	[Test]
	public async Task A_non_responsive_token_endpoint_fails_within_the_refresh_lifetime()
	{
		var oauth = new FakeSpotifyOAuthClient { Stall = true };
		using var manager = Manager(oauth, TimeSpan.Zero, refreshLifetime: TimeSpan.FromMilliseconds(200));

		Assert.That(async () => await manager.EnsureValidTokenAsync(CancellationToken.None),
			Throws.InstanceOf<SpotifyAuthTransientException>());
		Assert.That(manager.State, Is.EqualTo(SpotifyAuthenticationState.TemporarilyUnavailable));
		await Task.CompletedTask;
	}

	[Test]
	public async Task A_caller_that_cancels_its_wait_leaves_the_shared_refresh_running_for_the_others()
	{
		var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var oauth = new FakeSpotifyOAuthClient
		{
			BeforeRefresh = () =>
			{
				arrived.TrySetResult();
				return held.Task;
			}
		};
		using var manager = Manager(oauth, TimeSpan.Zero);

		using var giveUp = new CancellationTokenSource();
		var impatient = manager.EnsureValidTokenAsync(giveUp.Token);
		await arrived.Task.WaitAsync(TimeSpan.FromSeconds(5));
		var patient = manager.EnsureValidTokenAsync(CancellationToken.None);

		await giveUp.CancelAsync();
		Assert.That(async () => await impatient, Throws.InstanceOf<OperationCanceledException>());

		held.SetResult();
		await patient.WaitAsync(TimeSpan.FromSeconds(5));

		Assert.Multiple(() =>
		{
			Assert.That(oauth.RefreshCount, Is.EqualTo(1));
			Assert.That(manager.AccessToken, Is.EqualTo("access-1"));
		});
	}

	[Test]
	public async Task A_cancelled_caller_does_not_lock_the_manager_for_the_next_caller()
	{
		var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var oauth = new FakeSpotifyOAuthClient
		{
			BeforeRefresh = () =>
			{
				arrived.TrySetResult();
				return held.Task;
			}
		};
		using var manager = Manager(oauth, TimeSpan.Zero);

		using var giveUp = new CancellationTokenSource();
		var abandoned = manager.EnsureValidTokenAsync(giveUp.Token);
		await arrived.Task.WaitAsync(TimeSpan.FromSeconds(5));
		await giveUp.CancelAsync();
		Assert.That(async () => await abandoned, Throws.InstanceOf<OperationCanceledException>());

		held.SetResult();

		await manager.EnsureValidTokenAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
		Assert.That(manager.AccessToken, Is.EqualTo("access-1"));
	}

	[Test]
	public async Task Multiple_callers_share_one_successful_bounded_refresh()
	{
		var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var oauth = new FakeSpotifyOAuthClient
		{
			BeforeRefresh = () =>
			{
				arrived.TrySetResult();
				return held.Task;
			}
		};
		using var manager = Manager(oauth, TimeSpan.Zero);

		var first = manager.EnsureValidTokenAsync(CancellationToken.None);
		await arrived.Task.WaitAsync(TimeSpan.FromSeconds(5));
		var second = manager.EnsureValidTokenAsync(CancellationToken.None);
		var third = manager.EnsureValidTokenAsync(CancellationToken.None);

		held.SetResult();
		await Task.WhenAll(first, second, third).WaitAsync(TimeSpan.FromSeconds(5));

		Assert.That(oauth.RefreshCount, Is.EqualTo(1));
	}

	[Test]
	public async Task A_refresh_that_timed_out_is_retried_by_the_next_caller_and_then_succeeds()
	{
		var oauth = new FakeSpotifyOAuthClient();
		oauth.Results.Enqueue(FakeSpotifyOAuthClient.Unreachable());
		using var manager = Manager(oauth, TimeSpan.Zero, failureBackoff: TimeSpan.Zero);

		Assert.That(async () => await manager.EnsureValidTokenAsync(CancellationToken.None),
			Throws.InstanceOf<SpotifyAuthTransientException>());

		await manager.EnsureValidTokenAsync(CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(oauth.RefreshCount, Is.EqualTo(2));
			Assert.That(manager.AccessToken, Is.EqualTo("access-2"));
			Assert.That(manager.State, Is.EqualTo(SpotifyAuthenticationState.Valid));
			// The failed attempt must not have consumed the stored refresh token.
			Assert.That(oauth.UsedRefreshTokens, Has.Count.EqualTo(2).And.All.EqualTo("stored-refresh"));
		});
	}

	[Test]
	public async Task A_short_lived_issued_token_is_not_refreshed_again_on_the_next_call()
	{
		var oauth = new FakeSpotifyOAuthClient();
		oauth.Results.Enqueue(() => new SpotifyRefreshedToken("short-access", "short-refresh", 60));
		using var manager = Manager(oauth, TimeSpan.Zero, failureBackoff: TimeSpan.Zero);

		await manager.EnsureValidTokenAsync(CancellationToken.None);
		await manager.EnsureValidTokenAsync(CancellationToken.None);

		Assert.That(oauth.RefreshCount, Is.EqualTo(1));
	}

	[Test]
	public async Task A_transient_refresh_failure_reports_temporarily_unavailable()
	{
		var oauth = new FakeSpotifyOAuthClient();
		oauth.Results.Enqueue(FakeSpotifyOAuthClient.Unreachable());
		using var manager = Manager(oauth, TimeSpan.Zero);

		Assert.That(async () => await manager.EnsureValidTokenAsync(CancellationToken.None),
			Throws.InstanceOf<SpotifyAuthTransientException>());

		Assert.That(manager.State, Is.EqualTo(SpotifyAuthenticationState.TemporarilyUnavailable));
		await Task.CompletedTask;
	}

	[Test]
	public async Task An_invalid_grant_response_requires_reauthorization()
	{
		var oauth = new FakeSpotifyOAuthClient();
		oauth.Results.Enqueue(FakeSpotifyOAuthClient.Rejected());
		using var manager = Manager(oauth, TimeSpan.Zero);

		Assert.That(async () => await manager.EnsureValidTokenAsync(CancellationToken.None),
			Throws.InstanceOf<SpotifyAuthRejectedException>());

		Assert.That(manager.State, Is.EqualTo(SpotifyAuthenticationState.ReauthorizationRequired));
		await Task.CompletedTask;
	}

	[Test]
	public async Task Once_reauthorization_is_required_no_further_token_request_is_made()
	{
		var oauth = new FakeSpotifyOAuthClient();
		oauth.Results.Enqueue(FakeSpotifyOAuthClient.Rejected());
		using var manager = Manager(oauth, TimeSpan.Zero, failureBackoff: TimeSpan.Zero);

		Assert.That(async () => await manager.EnsureValidTokenAsync(CancellationToken.None),
			Throws.InstanceOf<SpotifyAuthRejectedException>());
		Assert.That(async () => await manager.EnsureValidTokenAsync(CancellationToken.None),
			Throws.InstanceOf<SpotifyAuthRejectedException>());

		// A dead grant must not keep hammering the token endpoint on every 1.5s poll.
		Assert.That(oauth.RefreshCount, Is.EqualTo(1));
		await Task.CompletedTask;
	}

	[Test]
	public async Task A_rotated_refresh_token_is_persisted_before_the_refresh_reports_success()
	{
		var writing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		_config = new RecordingIntegrationConfig
		{
			BeforeSecretWrite = _ =>
			{
				arrived.TrySetResult();
				return writing.Task;
			}
		};
		_persister = new SpotifyTokenPersister(_config, SilentLogger());

		var oauth = new FakeSpotifyOAuthClient();
		using var manager = Manager(oauth, TimeSpan.Zero);

		var refreshing = manager.EnsureValidTokenAsync(CancellationToken.None);
		await arrived.Task.WaitAsync(TimeSpan.FromSeconds(5));

		Assert.That(refreshing.IsCompleted, Is.False);

		writing.SetResult();
		await refreshing.WaitAsync(TimeSpan.FromSeconds(5));

		Assert.That(_config.Secrets[SpotifyConfigKeys.RefreshToken], Is.EqualTo("refresh-1"));
	}

	[Test]
	public async Task A_refresh_response_without_a_rotated_token_keeps_the_stored_refresh_token()
	{
		var oauth = new FakeSpotifyOAuthClient();
		oauth.Results.Enqueue(() => new SpotifyRefreshedToken("fresh-access", null, 0));
		using var manager = Manager(oauth, TimeSpan.Zero, failureBackoff: TimeSpan.Zero);

		await manager.EnsureValidTokenAsync(CancellationToken.None);
		var rewroteRefreshToken = _config.Secrets.ContainsKey(SpotifyConfigKeys.RefreshToken);

		await manager.EnsureValidTokenAsync(CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(rewroteRefreshToken, Is.False, "an unrotated refresh token must not be rewritten");
			Assert.That(oauth.UsedRefreshTokens, Has.Count.EqualTo(2).And.All.EqualTo("stored-refresh"));
		});
	}

	[Test]
	public async Task StopAsync_drains_an_in_flight_refresh_before_the_persistence_queue_closes()
	{
		var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var oauth = new FakeSpotifyOAuthClient
		{
			BeforeRefresh = () =>
			{
				arrived.TrySetResult();
				return held.Task;
			}
		};
		using var manager = Manager(oauth, TimeSpan.Zero);

		var refreshing = manager.EnsureValidTokenAsync(CancellationToken.None);
		await arrived.Task.WaitAsync(TimeSpan.FromSeconds(5));

		var stopping = manager.StopAsync(TimeSpan.FromSeconds(5));
		Assert.That(stopping.IsCompleted, Is.False);

		held.SetResult();
		await Task.WhenAll(refreshing, stopping).WaitAsync(TimeSpan.FromSeconds(5));
		await _persister.CompleteAsync();

		Assert.That(_config.Secrets[SpotifyConfigKeys.RefreshToken], Is.EqualTo("refresh-1"));
	}

	[Test]
	public async Task StopAsync_gives_up_within_its_timeout_when_a_refresh_will_not_finish()
	{
		var oauth = new FakeSpotifyOAuthClient { Stall = true };
		using var manager = Manager(oauth, TimeSpan.Zero, refreshLifetime: TimeSpan.FromSeconds(30));

		_ = manager.EnsureValidTokenAsync(CancellationToken.None);
		await WaitForRefreshAsync(oauth);

		// Shutdown must not inherit the token endpoint's problems.
		await manager.StopAsync(TimeSpan.FromMilliseconds(200)).WaitAsync(TimeSpan.FromSeconds(5));
	}

	[Test]
	public async Task StopAsync_rejects_a_refresh_that_has_not_started_yet()
	{
		var oauth = new FakeSpotifyOAuthClient();
		using var manager = Manager(oauth, TimeSpan.Zero);

		await manager.StopAsync(TimeSpan.FromSeconds(5));

		Assert.Multiple(() =>
		{
			Assert.That(async () => await manager.EnsureValidTokenAsync(CancellationToken.None),
				Throws.InstanceOf<SpotifyAuthTransientException>());
			Assert.That(oauth.RefreshCount, Is.EqualTo(0));
		});
	}

	[Test]
	public async Task A_transient_failure_is_not_retried_inside_the_backoff_window()
	{
		var oauth = new FakeSpotifyOAuthClient();
		oauth.Results.Enqueue(FakeSpotifyOAuthClient.Unreachable());
		using var manager = Manager(oauth, TimeSpan.Zero, failureBackoff: TimeSpan.FromMinutes(1));

		Assert.That(async () => await manager.EnsureValidTokenAsync(CancellationToken.None),
			Throws.InstanceOf<SpotifyAuthTransientException>());
		Assert.That(async () => await manager.EnsureValidTokenAsync(CancellationToken.None),
			Throws.InstanceOf<SpotifyAuthTransientException>());

		Assert.That(oauth.RefreshCount, Is.EqualTo(1));
		await Task.CompletedTask;
	}

	[Test]
	public async Task A_refresh_whose_rotation_could_not_be_stored_still_reports_a_usable_token()
	{
		_config = new RecordingIntegrationConfig
		{
			BeforeSecretWrite = _ => throw new IOException("disk full")
		};
		_persister = new SpotifyTokenPersister(_config, SilentLogger());

		var oauth = new FakeSpotifyOAuthClient();
		using var manager = Manager(oauth, TimeSpan.Zero);

		await manager.EnsureValidTokenAsync(CancellationToken.None);

		// The new token works; a storage problem must not blank Spotify for the hour it stays valid.
		Assert.Multiple(() =>
		{
			Assert.That(manager.AccessToken, Is.EqualTo("access-1"));
			Assert.That(manager.State, Is.EqualTo(SpotifyAuthenticationState.Valid));
		});
	}

	[Test]
	public async Task A_stale_401_report_skips_the_forced_refresh()
	{
		var oauth = new FakeSpotifyOAuthClient();
		using var manager = Manager(oauth, TimeSpan.FromHours(1));

		var refreshed = await manager.TryRefreshRejectedAccessTokenAsync("some-older-token",
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(refreshed, Is.True);
			Assert.That(oauth.RefreshCount, Is.EqualTo(0));
		});
	}

	[Test]
	public async Task A_forced_refresh_honours_the_failure_backoff()
	{
		var oauth = new FakeSpotifyOAuthClient();
		oauth.Results.Enqueue(FakeSpotifyOAuthClient.Unreachable());
		using var manager = Manager(oauth, TimeSpan.Zero, failureBackoff: TimeSpan.FromMinutes(1));

		Assert.That(async () => await manager.EnsureValidTokenAsync(CancellationToken.None),
			Throws.InstanceOf<SpotifyAuthTransientException>());

		// A 401 storm during a token-endpoint outage must not turn into a token request per tick.
		Assert.That(async () => await manager.TryRefreshRejectedAccessTokenAsync("stored-access",
				CancellationToken.None),
			Throws.InstanceOf<SpotifyAuthTransientException>());
		Assert.That(oauth.RefreshCount, Is.EqualTo(1));
		await Task.CompletedTask;
	}

	[Test]
	public async Task A_wedged_persistence_write_does_not_pin_the_refresh()
	{
		var writing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		_config = new RecordingIntegrationConfig { BeforeSecretWrite = _ => writing.Task };
		_persister = new SpotifyTokenPersister(_config, SilentLogger());

		var oauth = new FakeSpotifyOAuthClient();
		using var manager = Manager(oauth, TimeSpan.Zero, persistenceBudget: TimeSpan.FromMilliseconds(100));

		await manager.EnsureValidTokenAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

		Assert.Multiple(() =>
		{
			Assert.That(manager.AccessToken, Is.EqualTo("access-1"));
			Assert.That(manager.State, Is.EqualTo(SpotifyAuthenticationState.Valid));
		});

		writing.SetResult();
	}

	[Test]
	public void Temporary_refresh_failures_use_capped_exponential_backoff_and_reset_after_success()
	{
		var time = new ManualTimeProvider();
		var oauth = new FakeSpotifyOAuthClient();
		for (var i = 0; i < 8; i++)
		{
			oauth.Results.Enqueue(FakeSpotifyOAuthClient.Unreachable());
		}

		using var manager = Manager(oauth,
			TimeSpan.Zero,
			failureBackoff: TimeSpan.FromSeconds(3),
			timeProvider: time);

		Assert.ThrowsAsync<SpotifyAuthTransientException>(() => manager.EnsureValidTokenAsync(CancellationToken.None));
		Assert.That(oauth.RefreshCount, Is.EqualTo(1));

		var delays = new[] { 3, 6, 12, 24, 48, 60, 60 };
		for (var i = 0; i < delays.Length; i++)
		{
			time.Advance(TimeSpan.FromSeconds(delays[i]) - TimeSpan.FromMilliseconds(1));
			Assert.ThrowsAsync<SpotifyAuthTransientException>(() =>
				manager.EnsureValidTokenAsync(CancellationToken.None));
			Assert.That(oauth.RefreshCount, Is.EqualTo(i + 1));

			time.Advance(TimeSpan.FromMilliseconds(1));
			Assert.ThrowsAsync<SpotifyAuthTransientException>(() =>
				manager.EnsureValidTokenAsync(CancellationToken.None));
			Assert.That(oauth.RefreshCount, Is.EqualTo(i + 2));
		}

		time.Advance(TimeSpan.FromSeconds(60));
		Assert.DoesNotThrowAsync(() => manager.EnsureValidTokenAsync(CancellationToken.None));
		Assert.That(oauth.RefreshCount, Is.EqualTo(9));

		oauth.Results.Enqueue(FakeSpotifyOAuthClient.Unreachable());
		Assert.ThrowsAsync<SpotifyAuthTransientException>(() =>
			manager.TryRefreshRejectedAccessTokenAsync(manager.AccessToken, CancellationToken.None));
		Assert.That(oauth.RefreshCount, Is.EqualTo(10));
		time.Advance(TimeSpan.FromSeconds(3) - TimeSpan.FromMilliseconds(1));
		Assert.ThrowsAsync<SpotifyAuthTransientException>(() =>
			manager.TryRefreshRejectedAccessTokenAsync(manager.AccessToken, CancellationToken.None));
		Assert.That(oauth.RefreshCount, Is.EqualTo(10));
		time.Advance(TimeSpan.FromMilliseconds(1));
		Assert.DoesNotThrowAsync(() =>
			manager.TryRefreshRejectedAccessTokenAsync(manager.AccessToken, CancellationToken.None));
		Assert.That(oauth.RefreshCount, Is.EqualTo(11));
	}

	[Test]
	public void Retry_after_is_a_lower_bound_for_the_refresh_backoff()
	{
		var time = new ManualTimeProvider();
		var oauth = new FakeSpotifyOAuthClient();
		oauth.Results.Enqueue(FakeSpotifyOAuthClient.RateLimited(TimeSpan.FromSeconds(10)));
		using var manager = Manager(oauth,
			TimeSpan.Zero,
			failureBackoff: TimeSpan.FromSeconds(3),
			timeProvider: time);

		Assert.ThrowsAsync<SpotifyAuthTransientException>(() => manager.EnsureValidTokenAsync(CancellationToken.None));
		time.Advance(TimeSpan.FromSeconds(9));
		Assert.ThrowsAsync<SpotifyAuthTransientException>(() => manager.EnsureValidTokenAsync(CancellationToken.None));
		Assert.That(oauth.RefreshCount, Is.EqualTo(1));

		time.Advance(TimeSpan.FromSeconds(1));
		Assert.DoesNotThrowAsync(() => manager.EnsureValidTokenAsync(CancellationToken.None));
		Assert.That(oauth.RefreshCount, Is.EqualTo(2));
	}

	private SpotifyTokenManager Manager(
		ISpotifyOAuthClient oauth,
		TimeSpan expiresIn,
		TimeSpan? refreshLifetime = null,
		TimeSpan? failureBackoff = null,
		ILogger? logger = null,
		TimeSpan? persistenceBudget = null,
		TimeProvider? timeProvider = null)
		=> new(_entryId,
			"client-id",
			"client-secret",
			SpotifyTokenSnapshot.FromStored("stored-access",
				"stored-refresh",
				(timeProvider?.GetUtcNow().UtcDateTime ?? DateTime.UtcNow) + expiresIn,
				SpotifyTokenManager.RefreshMargin),
			oauth,
			_persister,
			logger ?? SilentLogger(),
			refreshLifetime ?? TimeSpan.FromSeconds(5),
			failureBackoff ?? TimeSpan.Zero,
			persistenceBudget,
			timeProvider);

	[Test]
	public async Task A_second_transient_refresh_failure_logs_debug_instead_of_warning()
	{
		var sink = new CollectingSink();
		var oauth = new FakeSpotifyOAuthClient();
		oauth.Results.Enqueue(FakeSpotifyOAuthClient.Unreachable());
		oauth.Results.Enqueue(FakeSpotifyOAuthClient.Unreachable());
		using var manager = Manager(oauth, TimeSpan.Zero, failureBackoff: TimeSpan.Zero, logger: Collecting(sink));

		Assert.That(async () => await manager.EnsureValidTokenAsync(CancellationToken.None),
			Throws.InstanceOf<SpotifyAuthTransientException>());
		Assert.That(async () => await manager.EnsureValidTokenAsync(CancellationToken.None),
			Throws.InstanceOf<SpotifyAuthTransientException>());

		// One Warning per episode, Debug afterwards - an outage must not write a Warning per attempt.
		var failing = sink.Events.Where(e => e.MessageTemplate.Text.Contains("failed after")).ToList();
		Assert.Multiple(() =>
		{
			Assert.That(failing.Count(e => e.Level == LogEventLevel.Warning), Is.EqualTo(1));
			Assert.That(failing.Count(e => e.Level == LogEventLevel.Debug), Is.EqualTo(1));
			Assert.That(manager.UnavailableSince, Is.Not.Null);
		});
	}

	[Test]
	public async Task A_recovered_refresh_logs_the_episode_end()
	{
		var sink = new CollectingSink();
		var oauth = new FakeSpotifyOAuthClient();
		oauth.Results.Enqueue(FakeSpotifyOAuthClient.Unreachable());
		using var manager = Manager(oauth, TimeSpan.Zero, failureBackoff: TimeSpan.Zero, logger: Collecting(sink));

		Assert.That(async () => await manager.EnsureValidTokenAsync(CancellationToken.None),
			Throws.InstanceOf<SpotifyAuthTransientException>());
		await manager.EnsureValidTokenAsync(CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(sink.Events.Count(e =>
					e.Level == LogEventLevel.Information && e.MessageTemplate.Text.Contains("recovered after")),
				Is.EqualTo(1));
			Assert.That(manager.UnavailableSince, Is.Null);
		});
	}

	private static async Task WaitForRefreshAsync(FakeSpotifyOAuthClient oauth)
	{
		for (var attempt = 0; attempt < 100 && oauth.RefreshCount == 0; attempt++)
		{
			await Task.Delay(20);
		}

		Assert.That(oauth.RefreshCount, Is.EqualTo(1), "the refresh never started");
	}

	private static Logger SilentLogger() => new LoggerConfiguration().CreateLogger();

	private static Logger Collecting(CollectingSink sink)
		=> new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(sink).CreateLogger();

	private sealed class CollectingSink : ILogEventSink
	{
		public List<LogEvent> Events { get; } = [];

		public void Emit(LogEvent logEvent) => Events.Add(logEvent);
	}
}
