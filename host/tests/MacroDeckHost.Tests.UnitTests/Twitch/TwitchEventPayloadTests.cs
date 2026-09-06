using System.Text.Json;
using MacroDeckHost.Integrations.Twitch;
using MacroDeck.Sdk.Events;

namespace MacroDeckHost.Tests.UnitTests.Twitch;

[TestFixture]
internal sealed class TwitchEventPayloadTests
{
	private static readonly TwitchAccount _account = new(Guid.NewGuid(),
		"client-id",
		"111",
		"streamer",
		"Streamer",
		"streamer",
		DateTimeOffset.UtcNow);

	private RecordingEventPublisher _publisher = null!;
	private TwitchEventEmitter _emitter = null!;

	[SetUp]
	public void SetUp()
	{
		_publisher = new RecordingEventPublisher();
		_emitter = new TwitchEventEmitter(_publisher);
	}

	[Test]
	public void A_snake_case_field_maps_onto_its_camel_case_name_without_an_alias()
	{
		var values = Build(TwitchEventIds.ChatSettingsUpdated,
			"""{"emote_mode":true,"follower_mode":false,"follower_mode_duration_minutes":30}""");

		Assert.Multiple(() =>
		{
			Assert.That(values["emoteMode"], Is.True);
			Assert.That(values["followerMode"], Is.False);
			Assert.That(values["followerModeDurationMinutes"], Is.EqualTo(30L));
		});
	}

	[Test]
	public void Twitchs_longer_names_are_aliased_to_the_declared_ones()
	{
		var values = Build(TwitchEventIds.RaidIncoming,
			"""
			{"from_broadcaster_user_id":"9","from_broadcaster_user_login":"raider",
			 "from_broadcaster_user_name":"Raider","viewers":42}
			""");

		Assert.Multiple(() =>
		{
			Assert.That(values["fromUserLogin"], Is.EqualTo("raider"));
			Assert.That(values["fromUserName"], Is.EqualTo("Raider"));
			Assert.That(values["viewers"], Is.EqualTo(42L));
		});
	}

	[Test]
	public void A_nested_value_and_a_reused_id_resolve_per_event()
	{
		var values = Build(TwitchEventIds.RewardRedeemed,
			"""
			{"id":"redemption-1","user_login":"viewer","user_name":"Viewer","user_input":"hello",
			 "status":"unfulfilled","reward":{"id":"reward-1","title":"Hydrate","cost":100}}
			""");

		Assert.Multiple(() =>
		{
			Assert.That(values["redemptionId"], Is.EqualTo("redemption-1"));
			Assert.That(values["rewardId"], Is.EqualTo("reward-1"));
			Assert.That(values["rewardTitle"], Is.EqualTo("Hydrate"));
			Assert.That(values["rewardCost"], Is.EqualTo(100L));
			Assert.That(values["userInput"], Is.EqualTo("hello"));
		});
	}

	[Test]
	public void A_message_object_is_reduced_to_its_text()
	{
		var values = Build(TwitchEventIds.SubscriptionMessage,
			"""{"user_login":"viewer","tier":"1000","cumulative_months":12,"message":{"text":"hi there"}}""");

		Assert.Multiple(() =>
		{
			Assert.That(values["message"], Is.EqualTo("hi there"));
			Assert.That(values["cumulativeMonths"], Is.EqualTo(12L));
		});
	}

	[Test]
	public void A_poll_result_names_the_winner()
	{
		var values = Build(TwitchEventIds.PollEnd,
			"""
			{"id":"poll-1","title":"Next game","status":"completed",
			 "choices":[{"id":"a","title":"Elden Ring","votes":3},{"id":"b","title":"Hades","votes":9}]}
			""");

		Assert.Multiple(() =>
		{
			Assert.That(values["pollId"], Is.EqualTo("poll-1"));
			Assert.That(values["winningChoice"], Is.EqualTo("Hades"));
			Assert.That(values["winningVotes"], Is.EqualTo(9L));
		});
	}

	[Test]
	public void A_prediction_result_resolves_the_winning_outcome_by_id()
	{
		var values = Build(TwitchEventIds.PredictionEnd,
			"""
			{"id":"p1","title":"Will we win","status":"resolved","winning_outcome_id":"o2",
			 "outcomes":[{"id":"o1","title":"Yes"},{"id":"o2","title":"No"}]}
			""");

		Assert.That(values["winningOutcome"], Is.EqualTo("No"));
	}

	[Test]
	public void Every_emitted_value_is_a_primitive()
	{
		var values = Build(TwitchEventIds.PollBegin,
			"""
			{"id":"poll-1","title":"Next game","ends_at":"2026-07-30T12:05:00Z",
			 "choices":[{"id":"a","title":"Elden Ring"},{"id":"b","title":"Hades"}]}
			""");

		Assert.Multiple(() =>
		{
			foreach (var (key, value) in values)
			{
				Assert.That(value,
					Is.Null.Or.InstanceOf<string>().Or.InstanceOf<bool>().Or.InstanceOf<long>()
						.Or.InstanceOf<double>(),
					key);
			}

			Assert.That(values["choices"], Is.EqualTo("Elden Ring, Hades"));
		});
	}

	[Test]
	public void Nothing_outside_the_declared_payload_is_emitted()
	{
		Assert.Multiple(() =>
		{
			foreach (var definition in TwitchEventDefinitions.All.Where(d => d.Id != TwitchEventIds.Any))
			{
				var declared = definition.PayloadParameters.Select(parameter => parameter.Name).ToList();
				var values = TwitchEventPayload.Build(declared, Element("""{"user_login":"viewer"}"""), _account);

				Assert.That(values.Keys, Is.EquivalentTo(declared), definition.Id);
			}
		});
	}

	[Test]
	public void Every_occurrence_says_which_account_it_came_from()
	{
		_emitter.Publish(_account, FakeEventSubClient.Notification("m1", "channel.follow", """{"user_login":"fan"}"""));

		Assert.Multiple(() =>
		{
			foreach (var (_, values) in _publisher.Published)
			{
				Assert.That(values!["account"], Is.EqualTo("111"));
				Assert.That(values["accountLogin"], Is.EqualTo("streamer"));
				Assert.That(values["accountName"], Is.EqualTo("Streamer"));
			}
		});
	}

	[Test]
	public void A_notification_raises_the_typed_event_and_the_advanced_one()
	{
		_emitter.Publish(_account,
			FakeEventSubClient.Notification("m1", "channel.cheer", """{"user_login":"fan","bits":100}"""));

		Assert.Multiple(() =>
		{
			Assert.That(_publisher.Published.Select(p => p.EventId),
				Is.EqualTo(new[] { TwitchEventIds.Cheer, TwitchEventIds.Any }));

			Assert.That(_publisher.Published[1].Values!["type"], Is.EqualTo("channel.cheer"));
			Assert.That(_publisher.Published[1].Values!["data"], Does.Contain("\"bits\":100"));
		});
	}

	[Test]
	public void An_unmodelled_subscription_type_still_reaches_the_advanced_event()
	{
		_emitter.Publish(_account, FakeEventSubClient.Notification("m1", "channel.something.new"));

		Assert.Multiple(() =>
		{
			Assert.That(_publisher.Published, Has.Count.EqualTo(1));
			Assert.That(_publisher.Published[0].EventId, Is.EqualTo(TwitchEventIds.Any));
			Assert.That(_publisher.Published[0].Values!["type"], Is.EqualTo("channel.something.new"));
		});
	}

	[Test]
	public void A_raid_is_incoming_or_outgoing_depending_on_which_side_the_account_is_on()
	{
		_emitter.Publish(_account,
			FakeEventSubClient.Notification("m1", "channel.raid", """{"to_broadcaster_user_id":"111"}"""));

		_emitter.Publish(_account,
			FakeEventSubClient.Notification("m2", "channel.raid", """{"to_broadcaster_user_id":"999"}"""));

		Assert.Multiple(() =>
		{
			Assert.That(_publisher.Published[0].EventId, Is.EqualTo(TwitchEventIds.RaidIncoming));
			Assert.That(_publisher.Published[2].EventId, Is.EqualTo(TwitchEventIds.RaidOutgoing));
		});
	}

	private static Dictionary<string, object?> Build(string eventId, string eventJson)
		=> TwitchEventPayload.Build(TwitchEventDefinitions.PayloadNames[eventId], Element(eventJson), _account);

	private static JsonElement Element(string json) => JsonDocument.Parse(json).RootElement.Clone();

	private sealed class RecordingEventPublisher : IEventPublisher
	{
		public List<(string EventId, IReadOnlyDictionary<string, object?>? Values)> Published { get; } = [];

		public void Publish(string eventId, IReadOnlyDictionary<string, object?>? parameters = null)
			=> Published.Add((eventId, parameters));
	}
}
