using MacroDeckHost.Integrations.Spotify;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Spotify;

[TestFixture]
internal sealed class SpotifyApiLimitStoreTests
{
	private static readonly Guid _entryId = Guid.Parse("33333333-3333-3333-3333-333333333333");

	[Test]
	public async Task An_open_episode_survives_a_restart()
	{
		var config = new RecordingIntegrationConfig();
		var store = Store(config);
		var status = new SpotifyApiLimitStatus(SpotifyApiLimitKind.RateLimit,
			DateTimeOffset.UtcNow,
			DateTimeOffset.UtcNow.AddHours(21.5));

		store.Save(_entryId, status);
		await store.FlushAsync();

		var restored = await Store(config).LoadAsync(_entryId);

		Assert.Multiple(() =>
		{
			Assert.That(restored?.Kind, Is.EqualTo(SpotifyApiLimitKind.RateLimit));
			Assert.That(restored?.PausedUntil, Is.EqualTo(status.PausedUntil).Within(TimeSpan.FromSeconds(1)));
			Assert.That(restored?.Since, Is.EqualTo(status.Since).Within(TimeSpan.FromSeconds(1)));
		});
	}

	[Test]
	public async Task An_expired_episode_is_not_restored()
	{
		var config = new RecordingIntegrationConfig();
		var store = Store(config);
		store.Save(_entryId,
			new SpotifyApiLimitStatus(SpotifyApiLimitKind.Quota,
				DateTimeOffset.UtcNow.AddHours(-2),
				DateTimeOffset.UtcNow.AddSeconds(-1)));
		await store.FlushAsync();

		Assert.That(await Store(config).LoadAsync(_entryId), Is.Null);
	}

	[Test]
	public async Task Recovering_clears_the_stored_episode()
	{
		var config = new RecordingIntegrationConfig();
		var store = Store(config);
		store.Save(_entryId,
			new SpotifyApiLimitStatus(SpotifyApiLimitKind.RateLimit,
				DateTimeOffset.UtcNow,
				DateTimeOffset.UtcNow.AddMinutes(30)));
		await store.FlushAsync();

		store.Clear(_entryId);
		await store.FlushAsync();

		Assert.That(await store.LoadAsync(_entryId), Is.Null);
	}

	[Test]
	public async Task The_same_episode_is_not_written_twice()
	{
		// Save runs whenever Spotify refuses, and a repeated 429 inside one episode reports the same
		// resume time; that must not put a config write on the refusal path.
		var config = new RecordingIntegrationConfig();
		var store = Store(config);
		var status = new SpotifyApiLimitStatus(SpotifyApiLimitKind.RateLimit,
			DateTimeOffset.UtcNow,
			DateTimeOffset.UtcNow.AddMinutes(30));

		store.Save(_entryId, status);
		store.Save(_entryId, status);
		store.Save(_entryId, status);
		await store.FlushAsync();

		Assert.That(config.WriteOrder.Count(k => k == SpotifyConfigKeys.ApiLimitUntil), Is.EqualTo(1));
	}

	private static SpotifyApiLimitStore Store(RecordingIntegrationConfig config)
		=> new(config, new LoggerConfiguration().CreateLogger());
}
