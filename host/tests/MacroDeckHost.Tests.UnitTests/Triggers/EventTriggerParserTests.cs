using System.Text.Json;
using MacroDeckHost.Application.Triggers;

namespace MacroDeckHost.Tests.UnitTests.Triggers;

[TestFixture]
public class EventTriggerParserTests
{
	private static string Flow(string value, string? op)
	{
		var parameter = new Dictionary<string, string?>(StringComparer.Ordinal)
		{
			["name"] = "sceneName",
			["value"] = value,
			["operator"] = op
		};

		var flow = new object[]
		{
			new
			{
				triggerId = "t1",
				triggerType = "onEvent",
				@event = new
				{
					providerId = "obs",
					eventId = "scene-changed",
					parameters = new[] { parameter }
				}
			}
		};

		return JsonSerializer.Serialize(flow);
	}

	private static EventConfigurationValue Configured(string value, string? op)
		=> EventTriggerParser.ParseFlows(EventTriggerOwner.ForWidget(Guid.NewGuid()), Flow(value, op))
			.Single()
			.Configuration["sceneName"];

	[Test]
	public void An_authored_operator_reaches_the_configuration_value()
	{
		Assert.That(Configured("Live", ">").Operator, Is.EqualTo(">"));
	}

	[Test]
	public void No_operator_normalises_to_equality()
	{
		Assert.That(Configured("Live", null).Operator, Is.EqualTo("=="));
	}

	[TestCase("~=")]
	[TestCase("contains")]
	public void An_operator_outside_the_vocabulary_normalises_to_equality(string op)
	{
		Assert.That(Configured("Live", op).Operator, Is.EqualTo("=="));
	}

	[TestCase("==")]
	[TestCase("!=")]
	[TestCase(">")]
	[TestCase("<")]
	[TestCase(">=")]
	[TestCase("<=")]
	public void Every_supported_operator_round_trips(string op)
	{
		Assert.That(Configured("Live", op).Operator, Is.EqualTo(op));
	}

	// B1 - the state vocabulary round-trips unchanged, just like the six comparison operators above.
	[TestCase("isEmpty")]
	[TestCase("isNotEmpty")]
	[TestCase("isAvailable")]
	[TestCase("isNotAvailable")]
	public void A_state_operator_round_trips_unchanged(string op)
	{
		Assert.That(Configured("Live", op).Operator, Is.EqualTo(op));
	}

	[Test]
	public void A_parameter_with_a_blank_name_is_skipped()
	{
		const string flow =
			"""
			[{"triggerId":"t1","triggerType":"onEvent","event":{"providerId":"obs","eventId":"scene-changed","parameters":[{"name":"","value":"Live"},{"name":"   ","value":"Live"}]}}]
			""";

		var subscriptions = EventTriggerParser.ParseFlows(EventTriggerOwner.ForWidget(Guid.NewGuid()), flow);

		Assert.That(subscriptions.Single().Configuration, Is.Empty);
	}

	[Test]
	public void An_automations_bare_flow_array_is_parsed_the_same_way()
	{
		var subscription = EventTriggerParser.ParseAutomation(EventTriggerOwner.ForAutomation(Guid.NewGuid()),
			Flow("Live", ">="));

		Assert.That(subscription!.Configuration["sceneName"].Operator, Is.EqualTo(">="));
	}

	[Test]
	public void A_stored_trigger_with_a_split_provider_and_event_id_produces_the_qualified_id()
	{
		var subscription = EventTriggerParser.ParseFlows(EventTriggerOwner.ForWidget(Guid.NewGuid()),
			Flow("Live", ">")).Single();

		Assert.That(subscription.EventId, Is.EqualTo("obs::scene-changed"));
	}

	[TestCase("bad id", "scene-changed")]
	[TestCase("obs", "")]
	public void A_malformed_stored_provider_or_event_id_is_skipped_rather_than_thrown(string providerId, string eventId)
	{
		var flow = JsonSerializer.Serialize(new object[]
		{
			new
			{
				triggerId = "t1",
				triggerType = "onEvent",
				@event = new { providerId, eventId }
			}
		});

		var subscriptions = EventTriggerParser.ParseFlows(EventTriggerOwner.ForWidget(Guid.NewGuid()), flow);

		Assert.That(subscriptions, Is.Empty);
	}
}
