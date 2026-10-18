using System.Globalization;
using MacroDeckHost.Integrations.Twitch.Auth;
using MacroDeckHost.Integrations.Twitch.Configuration;
using MacroDeckHost.Integrations.Twitch.Connections;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Twitch;

[TestFixture]
internal sealed class TwitchAccountConnectionStateChangeTests
{
	private TwitchTokenPersister _persister = null!;
	private TwitchAccountConnection _connection = null!;
	private int _changes;

	[SetUp]
	public void SetUp()
	{
		var logger = new LoggerConfiguration().CreateLogger();
		_persister = new TwitchTokenPersister(new RecordingIntegrationConfig(), logger);
		var tokens = new TwitchTokenProvider(Guid.NewGuid(),
			"client-id",
			FakeTwitchOAuthClient.Tokens("access", "refresh", TimeSpan.FromHours(4)),
			new FakeTwitchOAuthClient(),
			_persister,
			logger);
		var account = new TwitchAccount(Guid.NewGuid(),
			"client-id",
			"1234",
			"streamer",
			"Streamer",
			"streamer",
			DateTimeOffset.UtcNow);
		_changes = 0;
		_connection = new TwitchAccountConnection(account,
			tokens,
			new FakeTwitchOAuthClient(),
			new FakeTwitchHelixClient(),
			null,
			logger,
			null,
			() => _changes++);
	}

	[TearDown]
	public void TearDown()
	{
		_connection.Dispose();
		_persister.Dispose();
	}

	[Test]
	public void Going_live_reports_a_state_change()
	{
		_connection.HandleNotification(FakeEventSubClient.Notification("m1",
			"stream.online",
			"""{"started_at":"2026-10-18T10:00:00Z"}"""));

		Assert.That(_changes, Is.EqualTo(1));
	}

	[Test]
	public void A_notification_that_leaves_the_state_as_it_was_reports_nothing()
	{
		_connection.HandleNotification(FakeEventSubClient.Notification("m1", "stream.offline"));
		_connection.HandleNotification(FakeEventSubClient.Notification("m2", "stream.offline"));

		Assert.That(_changes, Is.EqualTo(1));
	}

	[Test]
	public async Task An_account_loaded_by_the_manager_reports_its_state_changes_to_the_callback_given_on_reload()
	{
		var reported = 0;
		var manager = new TwitchAccountManager(() => new FakeTwitchOAuthClient(),
			new LoggerConfiguration().CreateLogger(),
			(_, _) => new FakeTwitchHelixClient());
		var config = new RecordingIntegrationConfig();
		config.AddEntry("Twitch (streamer)",
			new Dictionary<string, string?>(StringComparer.Ordinal)
			{
				[TwitchConfigKeys.ClientId] = "client-id",
				[TwitchConfigKeys.UserId] = "111",
				[TwitchConfigKeys.Login] = "streamer",
				[TwitchConfigKeys.DisplayName] = "Streamer",
				[TwitchConfigKeys.Scopes] = TwitchScopes.Requested,
				[TwitchConfigKeys.ExpiresAt] =
					DateTimeOffset.UtcNow.AddHours(4).ToString("o", CultureInfo.InvariantCulture)
			},
			new Dictionary<string, string>(StringComparer.Ordinal)
			{
				[TwitchConfigKeys.AccessToken] = "access",
				[TwitchConfigKeys.RefreshToken] = "refresh"
			});
		await manager.ReloadAsync(config, null, () => reported++);

		manager.Connections.Single().HandleNotification(FakeEventSubClient.Notification("m1", "stream.offline"));

		Assert.That(reported, Is.EqualTo(1));
	}

	[Test]
	public void A_poll_that_brings_a_new_follower_count_reports_a_state_change_and_an_unchanged_one_does_not()
	{
		_connection.Merge(state => state with { FollowerCount = 10 });
		_connection.Merge(state => state with { FollowerCount = 10 });

		Assert.That(_changes, Is.EqualTo(1));
	}
}
