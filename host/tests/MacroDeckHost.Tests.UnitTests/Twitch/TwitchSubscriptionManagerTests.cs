using MacroDeckHost.Integrations.Twitch;
using MacroDeckHost.Integrations.Twitch.Auth;
using MacroDeckHost.Integrations.Twitch.Protocol;
using Serilog;
using Serilog.Core;

namespace MacroDeckHost.Tests.UnitTests.Twitch;

[TestFixture]
internal sealed class TwitchSubscriptionManagerTests
{
	private static readonly string[] _raidConditionKeys = ["to_broadcaster_user_id", "from_broadcaster_user_id"];

	private FakeTwitchHelixClient _helix = null!;

	[SetUp]
	public void SetUp()
	{
		_helix = new FakeTwitchHelixClient();
	}

	[Test]
	public async Task The_first_subscription_is_the_one_that_needs_no_scope()
	{
		await Manager().SubscribeAllAsync("session-1", CancellationToken.None);

		Assert.That(_helix.Subscriptions[0].Type, Is.EqualTo("stream.online"));
	}

	[Test]
	public async Task Every_catalogued_event_is_subscribed_against_the_welcomed_session()
	{
		var report = await Manager().SubscribeAllAsync("session-1", CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(_helix.Subscriptions, Has.Count.EqualTo(TwitchEventCatalog.All.Count));
			Assert.That(_helix.Subscriptions.Select(s => s.SessionId), Is.All.EqualTo("session-1"));
			Assert.That(report.Created, Has.Count.EqualTo(TwitchEventCatalog.All.Count));
			Assert.That(report.MissingScope, Is.Empty);
		});
	}

	[Test]
	public async Task A_refused_scope_does_not_abort_the_batch()
	{
		_helix.SubscriptionResults["channel.follow"] = TwitchSubscriptionResult.MissingScope;

		var report = await Manager().SubscribeAllAsync("session-1", CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(report.MissingScope, Is.EqualTo(new[] { TwitchEventIds.Follow }));
			Assert.That(report.Created, Has.Count.EqualTo(TwitchEventCatalog.All.Count - 1));
		});
	}

	[Test]
	public async Task A_duplicate_counts_as_created()
	{
		_helix.SubscriptionResults["channel.cheer"] = TwitchSubscriptionResult.Duplicate;

		var report = await Manager().SubscribeAllAsync("session-1", CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(report.Created, Does.Contain(TwitchEventIds.Cheer));
			Assert.That(report.Failed, Is.Empty);
		});
	}

	[Test]
	public async Task A_removed_version_is_reported_as_unsupported()
	{
		_helix.SubscriptionResults["channel.ban"] = TwitchSubscriptionResult.Unsupported;

		var report = await Manager().SubscribeAllAsync("session-1", CancellationToken.None);

		Assert.That(report.Unsupported, Is.EqualTo(new[] { TwitchEventIds.Ban }));
	}

	[Test]
	public async Task A_scope_that_was_not_granted_is_not_even_requested()
	{
		var granted = new[] { TwitchScopes.UserReadChat };

		var report = await Manager(granted).SubscribeAllAsync("session-1", CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(_helix.Subscriptions.Select(s => s.Type), Does.Not.Contain("channel.follow"));
			Assert.That(report.MissingScope, Does.Contain(TwitchEventIds.Follow));

			Assert.That(_helix.Subscriptions.Select(s => s.Type), Does.Contain("stream.online"));
			Assert.That(_helix.Subscriptions.Select(s => s.Type), Does.Contain("channel.chat.notification"));
		});
	}

	[Test]
	public async Task An_unknown_granted_scope_list_asks_for_everything()
	{
		await Manager([]).SubscribeAllAsync("session-1", CancellationToken.None);

		Assert.That(_helix.Subscriptions, Has.Count.EqualTo(TwitchEventCatalog.All.Count));
	}

	[Test]
	public async Task Each_condition_kind_carries_the_ids_twitch_expects()
	{
		await Manager().SubscribeAllAsync("session-1", CancellationToken.None);

		var byType = _helix.Subscriptions.ToLookup(s => s.Type, StringComparer.Ordinal);
		Assert.Multiple(() =>
		{
			Assert.That(byType["channel.follow"].Single().Condition,
				Is.EquivalentTo(new Dictionary<string, string>(StringComparer.Ordinal)
				{
					["broadcaster_user_id"] = "111",
					["moderator_user_id"] = "111"
				}));

			Assert.That(byType["channel.chat.notification"].Single().Condition,
				Is.EquivalentTo(new Dictionary<string, string>(StringComparer.Ordinal)
				{
					["broadcaster_user_id"] = "111",
					["user_id"] = "111"
				}));

			var raids = byType["channel.raid"].ToList();
			Assert.That(raids, Has.Count.EqualTo(2));
			Assert.That(raids.Select(r => r.Condition.Keys.Single()), Is.EquivalentTo(_raidConditionKeys));
		});
	}

	private TwitchSubscriptionManager Manager(IReadOnlyList<string>? granted = null)
		=> new(_helix, "111", () => granted ?? TwitchScopes.All, SilentLogger());

	private static Logger SilentLogger() => new LoggerConfiguration().CreateLogger();
}
