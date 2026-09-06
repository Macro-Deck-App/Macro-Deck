using System.Globalization;
using MacroDeckHost.Integrations.Spotify;
using Serilog;
using Serilog.Core;

namespace MacroDeckHost.Tests.UnitTests.Spotify;

[TestFixture]
internal sealed class SpotifyTokenPersisterTests
{
	private static readonly Guid _entryId = Guid.Parse("11111111-1111-1111-1111-111111111111");
	private static readonly string[] _twoRotations = ["refresh-1", "access-1", "refresh-2", "access-2"];

	[Test]
	public async Task Persists_rotated_token_writing_the_refresh_token_first()
	{
		var config = new RecordingIntegrationConfig();
		var persister = new SpotifyTokenPersister(config, SilentLogger());
		var expiresAt = new DateTime(2026, 7, 23, 12, 0, 0, DateTimeKind.Utc);

		persister.Enqueue(_entryId, "new-access", "new-refresh", expiresAt);
		await persister.FlushAsync();

		Assert.Multiple(() =>
		{
			Assert.That(config.Secrets[SpotifyConfigKeys.RefreshToken], Is.EqualTo("new-refresh"));
			Assert.That(config.Secrets[SpotifyConfigKeys.AccessToken], Is.EqualTo("new-access"));
			Assert.That(config.Strings[SpotifyConfigKeys.ExpiresAt],
				Is.EqualTo(expiresAt.ToString("o", CultureInfo.InvariantCulture)));
			Assert.That(config.WriteOrder[0], Is.EqualTo(SpotifyConfigKeys.RefreshToken));
		});
	}

	[Test]
	public async Task Rotated_refresh_token_survives_a_simulated_restart()
	{
		var store = new RecordingIntegrationConfig();
		store.Secrets[SpotifyConfigKeys.RefreshToken] = "old-refresh";
		var persister = new SpotifyTokenPersister(store, SilentLogger());

		persister.Enqueue(_entryId, "new-access", "new-refresh", DateTime.UtcNow);
		await persister.FlushAsync();

		var afterRestart = await store.GetSecretAsync(_entryId, SpotifyConfigKeys.RefreshToken);
		Assert.That(afterRestart, Is.EqualTo("new-refresh"));
	}

	[Test]
	public async Task Keeps_existing_refresh_token_when_the_response_omits_one()
	{
		var config = new RecordingIntegrationConfig();
		config.Secrets[SpotifyConfigKeys.RefreshToken] = "existing-refresh";
		var persister = new SpotifyTokenPersister(config, SilentLogger());

		persister.Enqueue(_entryId, "new-access", refreshToken: null, DateTime.UtcNow);
		await persister.FlushAsync();

		Assert.Multiple(() =>
		{
			Assert.That(config.Secrets[SpotifyConfigKeys.RefreshToken], Is.EqualTo("existing-refresh"));
			Assert.That(config.Secrets[SpotifyConfigKeys.AccessToken], Is.EqualTo("new-access"));
			Assert.That(config.WriteOrder, Does.Not.Contain(SpotifyConfigKeys.RefreshToken));
		});
	}

	[Test]
	public async Task FlushAsync_awaits_an_in_flight_write()
	{
		var release = new TaskCompletionSource();
		var config = new RecordingIntegrationConfig { BeforeSecretWrite = _ => release.Task };
		var persister = new SpotifyTokenPersister(config, SilentLogger());

		persister.Enqueue(_entryId, "new-access", "new-refresh", DateTime.UtcNow);
		var flush = persister.FlushAsync();
		Assert.That(flush.IsCompleted, Is.False, "flush must not complete while a write is still pending");

		release.SetResult();
		await flush.WaitAsync(TimeSpan.FromSeconds(5));

		Assert.That(config.Secrets[SpotifyConfigKeys.RefreshToken], Is.EqualTo("new-refresh"));
	}

	[Test]
	public async Task Concurrent_rotations_do_not_interleave()
	{
		var active = 0;
		var maxConcurrent = 0;
		var sync = new object();
		var config = new RecordingIntegrationConfig
		{
			BeforeSecretWrite = async _ =>
			{
				lock (sync)
				{
					active++;
					maxConcurrent = Math.Max(maxConcurrent, active);
				}

				await Task.Delay(10);
				lock (sync)
				{
					active--;
				}
			}
		};
		var persister = new SpotifyTokenPersister(config, SilentLogger());

		persister.Enqueue(_entryId, "access-1", "refresh-1", DateTime.UtcNow);
		persister.Enqueue(_entryId, "access-2", "refresh-2", DateTime.UtcNow);
		await persister.FlushAsync();

		Assert.Multiple(() =>
		{
			Assert.That(maxConcurrent, Is.EqualTo(1), "writes from two rotations must be serialized");
			Assert.That(config.Secrets[SpotifyConfigKeys.RefreshToken], Is.EqualTo("refresh-2"));
		});
	}

	[Test]
	public async Task CompleteAsync_drains_every_accepted_rotation_in_fifo_order_and_rejects_late_writes()
	{
		var firstWriteStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var releaseFirstWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var secretWriteCount = 0;
		var config = new RecordingIntegrationConfig
		{
			BeforeSecretWrite = async _ =>
			{
				if (Interlocked.Increment(ref secretWriteCount) == 1)
				{
					firstWriteStarted.TrySetResult();
					await releaseFirstWrite.Task;
				}
			}
		};
		var persister = new SpotifyTokenPersister(config, SilentLogger());

		Assert.That(persister.Enqueue(_entryId, "access-1", "refresh-1", DateTime.UtcNow), Is.True);
		await firstWriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
		Assert.That(persister.Enqueue(_entryId, "access-2", "refresh-2", DateTime.UtcNow), Is.True);

		var completing = persister.CompleteAsync();
		Assert.That(persister.Enqueue(_entryId, "access-too-late", "refresh-too-late", DateTime.UtcNow), Is.False);
		Assert.That(completing.IsCompleted, Is.False);

		releaseFirstWrite.SetResult();
		await completing.WaitAsync(TimeSpan.FromSeconds(5));

		Assert.Multiple(() =>
		{
			Assert.That(config.Secrets[SpotifyConfigKeys.RefreshToken], Is.EqualTo("refresh-2"));
			Assert.That(config.Secrets[SpotifyConfigKeys.AccessToken], Is.EqualTo("access-2"));
			Assert.That(config.WriteValues, Is.EqualTo(_twoRotations));
		});
	}

	[Test]
	public void CompleteAsync_surfaces_a_persistence_failure()
	{
		var config = new RecordingIntegrationConfig
		{
			BeforeSecretWrite = _ => throw new IOException("disk unavailable")
		};
		var persister = new SpotifyTokenPersister(config, SilentLogger());

		persister.Enqueue(_entryId, "access", "refresh", DateTime.UtcNow);

		Assert.That(async () => await persister.CompleteAsync(), Throws.InstanceOf<IOException>());
	}

	[Test]
	public void A_failed_rotation_does_not_prevent_a_newer_rotation_from_becoming_durable()
	{
		var calls = 0;
		var config = new RecordingIntegrationConfig
		{
			BeforeSecretWrite = _ => Interlocked.Increment(ref calls) == 1
				? throw new IOException("one transient write failure")
				: Task.CompletedTask
		};
		var persister = new SpotifyTokenPersister(config, SilentLogger());

		persister.Enqueue(_entryId, "access-1", "refresh-1", DateTime.UtcNow);
		persister.Enqueue(_entryId, "access-2", "refresh-2", DateTime.UtcNow);

		Assert.That(async () => await persister.CompleteAsync(), Throws.InstanceOf<IOException>());
		Assert.Multiple(() =>
		{
			Assert.That(config.Secrets[SpotifyConfigKeys.RefreshToken], Is.EqualTo("refresh-2"));
			Assert.That(config.Secrets[SpotifyConfigKeys.AccessToken], Is.EqualTo("access-2"));
		});
	}

	[Test]
	public async Task EnqueueAndWaitAsync_completes_only_after_the_write_landed()
	{
		var writing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var config = new RecordingIntegrationConfig
		{
			BeforeSecretWrite = _ =>
			{
				arrived.TrySetResult();
				return writing.Task;
			}
		};
		using var persister = new SpotifyTokenPersister(config, SilentLogger());

		var queued = persister.EnqueueAndWaitAsync(_entryId, "access", "refresh", DateTime.UtcNow);
		await arrived.Task.WaitAsync(TimeSpan.FromSeconds(5));

		Assert.That(queued.IsCompleted, Is.False);

		writing.SetResult();
		Assert.That(await queued.WaitAsync(TimeSpan.FromSeconds(5)), Is.True);
	}

	[Test]
	public async Task EnqueueAndWaitAsync_waits_behind_an_earlier_write_in_fifo_order()
	{
		var config = new RecordingIntegrationConfig();
		using var persister = new SpotifyTokenPersister(config, SilentLogger());

		persister.Enqueue(_entryId, "first-access", "first-refresh", DateTime.UtcNow);
		var second = persister.EnqueueAndWaitAsync(_entryId, "second-access", "second-refresh", DateTime.UtcNow);

		Assert.That(await second.WaitAsync(TimeSpan.FromSeconds(5)), Is.True);
		Assert.Multiple(() =>
		{
			Assert.That(config.Secrets[SpotifyConfigKeys.RefreshToken], Is.EqualTo("second-refresh"));
			Assert.That(config.WriteValues, Does.Contain("first-refresh"));
		});
	}

	[Test]
	public async Task EnqueueAndWaitAsync_reports_false_after_the_queue_closed()
	{
		var config = new RecordingIntegrationConfig();
		using var persister = new SpotifyTokenPersister(config, SilentLogger());
		await persister.CompleteAsync();

		var rejected = await persister.EnqueueAndWaitAsync(_entryId, "access", "refresh", DateTime.UtcNow);

		Assert.Multiple(() =>
		{
			Assert.That(rejected, Is.False);
			Assert.That(config.WriteOrder, Is.Empty);
		});
	}

	[Test]
	public async Task EnqueueAndWaitAsync_reports_false_when_the_write_failed()
	{
		var config = new RecordingIntegrationConfig
		{
			BeforeSecretWrite = _ => throw new IOException("disk full")
		};
		using var persister = new SpotifyTokenPersister(config, SilentLogger());

		Assert.That(await persister.EnqueueAndWaitAsync(_entryId, "access", "refresh", DateTime.UtcNow), Is.False);
	}

	private static Logger SilentLogger() => new LoggerConfiguration().CreateLogger();
}
