using MacroDeck.Sdk.Actions;
using MacroDeckHost.Integrations.YouTube;
using MacroDeckHost.Integrations.YouTube.Protocol;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.YouTube;

[TestFixture]
internal sealed class YouTubeEventDefinitionsTests
{
	[Test]
	public void The_documented_events_are_declared()
	{
		Assert.That(YouTubeEventDefinitions.All.Select(definition => definition.Id), Is.EquivalentTo(new[]
		{
			"stream-online", "stream-offline", "super-chat", "super-sticker", "new-member", "member-milestone",
			"membership-gift", "event"
		}));
	}

	[Test]
	public void Event_ids_are_unique()
		=> Assert.That(YouTubeEventDefinitions.All.Select(definition => definition.Id), Is.Unique);

	[Test]
	public void Every_configuration_parameter_has_a_payload_parameter_of_the_same_name()
	{
		Assert.Multiple(() =>
		{
			foreach (var definition in YouTubeEventDefinitions.All)
			{
				var payloadNames = definition.PayloadParameters.Select(parameter => parameter.Name).ToList();
				foreach (var configuration in definition.ConfigurationParameters)
				{
					Assert.That(payloadNames, Has.Member(configuration.Name), $"{definition.Id}.{configuration.Name}");
				}
			}
		});
	}

	[Test]
	public void Every_event_can_be_filtered_by_channel_and_says_which_one_it_came_from()
	{
		Assert.Multiple(() =>
		{
			foreach (var definition in YouTubeEventDefinitions.All)
			{
				Assert.That(definition.ConfigurationParameters.Select(parameter => parameter.Name),
					Has.Member("account"),
					definition.Id);
				Assert.That(definition.PayloadParameters.Select(parameter => parameter.Name),
					Is.SupersetOf(new[] { "account", "accountName" }),
					definition.Id);
			}
		});
	}

	[Test]
	public void No_configuration_parameter_is_required_and_each_says_what_empty_means()
	{
		Assert.Multiple(() =>
		{
			foreach (var definition in YouTubeEventDefinitions.All)
			{
				foreach (var configuration in definition.ConfigurationParameters)
				{
					Assert.That(configuration.Required, Is.False, $"{definition.Id}.{configuration.Name}");

					if (configuration.Type is ActionParameterType.DynamicChoice)
					{
						Assert.That(TestLocalization.Resolve(configuration.Placeholder),
							Is.Not.Null.And.Not.Empty,
							$"{definition.Id}.{configuration.Name}");
					}
				}
			}
		});
	}

	[Test]
	public void Every_event_is_named_categorised_and_described()
	{
		Assert.Multiple(() =>
		{
			foreach (var definition in YouTubeEventDefinitions.All)
			{
				Assert.That(TestLocalization.Resolve(definition.Name), Is.Not.Null.And.Not.Empty, definition.Id);
				Assert.That(TestLocalization.Resolve(definition.Category), Is.Not.Null.And.Not.Empty, definition.Id);
				Assert.That(TestLocalization.Resolve(definition.Description), Is.Not.Null.And.Not.Empty, definition.Id);

				foreach (var parameter in definition.ConfigurationParameters.Concat(definition.PayloadParameters))
				{
					Assert.That(Enum.IsDefined(parameter.Type), Is.True, $"{definition.Id}.{parameter.Name}");
					Assert.That(TestLocalization.Resolve(parameter.Label),
						Is.Not.Null.And.Not.Empty,
						$"{definition.Id}.{parameter.Name}");
				}
			}
		});
	}

	[Test]
	public void Every_published_payload_matches_its_declaration()
	{
		var publisher = new RecordingYouTubeEventPublisher();
		var connection = new YouTubeAccountConnection(YouTubeTestSupport.Account(),
			null,
			null,
			new FakeYouTubeApiClient(),
			new YouTubeQuotaBudget(),
			new YouTubeEventEmitter(publisher),
			YouTubeTestSupport.Silent,
			options: YouTubeTestSupport.ManualOptions());
		var author = YouTubeTestSupport.Author();

		connection.OnTransition(new YouTubeLiveTransition(true, YouTubeAccountState.Unknown with { IsLive = true }));
		connection.OnTransition(new YouTubeLiveTransition(false, YouTubeAccountState.Unknown));
		connection.OnChatMessages(
		[
			new("1", "superChatEvent", null, "a", author)
			{
				SuperChat = new YouTubeSuperChat(1, "USD", "$1", null, 1)
			},
			new("2", "superStickerEvent", null, "a", author)
			{
				SuperSticker = new YouTubeSuperSticker(1, "USD", "$1", 1,
					null, null)
			},
			new("3", "newSponsorEvent", null, "a", author),
			new("4", "memberMilestoneChatEvent", null, "a", author),
			new("5", "membershipGiftingEvent", null, "a", author)
		], true);
		connection.Dispose();

		var declared = YouTubeEventDefinitions.All.ToDictionary(definition => definition.Id,
			definition => definition.PayloadParameters.Select(parameter => parameter.Name).ToHashSet());

		Assert.Multiple(() =>
		{
			Assert.That(publisher.Ids.Distinct(), Is.EquivalentTo(declared.Keys));
			foreach (var (eventId, payload) in publisher.Published)
			{
				Assert.That(payload.Keys, Is.EquivalentTo(declared[eventId]), eventId);
			}
		});
	}
}
