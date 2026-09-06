using System.Text.Json;
using MacroDeckHost.Integrations.HomeAssistant;
using MacroDeckHost.Integrations.HomeAssistant.Protocol;

namespace MacroDeckHost.Tests.UnitTests.HomeAssistant;

[TestFixture]
internal sealed class HomeAssistantEventPayloadTests
{
	[Test]
	public void StateChanged_carries_the_entity_domain_states_and_attributes_as_json()
	{
		var newState = State("light.kitchen",
			"on",
			"""{"friendly_name":"Kitchen Lamp","unit_of_measurement":"lx","brightness":200}""");

		var payload = HomeAssistantEventPayload.StateChanged("light.kitchen", newState, "off", "Kitchen");

		Assert.Multiple(() =>
		{
			Assert.That(payload["entityId"], Is.EqualTo("light.kitchen"));
			Assert.That(payload["domain"], Is.EqualTo("light"));
			Assert.That(payload["toState"], Is.EqualTo("on"));
			Assert.That(payload["fromState"], Is.EqualTo("off"));
			Assert.That(payload["friendlyName"], Is.EqualTo("Kitchen Lamp"));
			Assert.That(payload["area"], Is.EqualTo("Kitchen"));
			Assert.That(payload["unit"], Is.EqualTo("lx"));
			Assert.That(payload["attributes"], Is.InstanceOf<string>());
			Assert.That((string)payload["attributes"]!, Does.Contain("\"brightness\":200"));
		});
	}

	[Test]
	public void StateChanged_answers_empty_strings_rather_than_null_for_a_removed_entity()
	{
		// The entity was deleted: new_state is absent from the event, so every field it would have
		// supplied reads as empty rather than null - a flow's $event access must not throw.
		var payload = HomeAssistantEventPayload.StateChanged("light.kitchen", null, "on", null);

		Assert.Multiple(() =>
		{
			Assert.That(payload["toState"], Is.EqualTo(string.Empty));
			Assert.That(payload["friendlyName"], Is.EqualTo(string.Empty));
			Assert.That(payload["area"], Is.EqualTo(string.Empty));
			Assert.That(payload["unit"], Is.EqualTo(string.Empty));
			Assert.That(payload["attributes"], Is.EqualTo("{}"));
			Assert.That(payload["fromState"], Is.EqualTo("on"));
		});
	}

	[Test]
	public void StateChanged_answers_empty_from_state_when_there_was_no_previous_state()
	{
		var payload = HomeAssistantEventPayload.StateChanged("light.kitchen", State("light.kitchen", "on"), null, null);

		Assert.That(payload["fromState"], Is.EqualTo(string.Empty));
	}

	[Test]
	public void Event_carries_the_type_the_entity_and_the_raw_data()
	{
		using var document = JsonDocument.Parse("""{ "entity_id": "automation.morning", "source": "manual" }""");

		var payload = HomeAssistantEventPayload.Event("automation_triggered", document.RootElement);

		Assert.Multiple(() =>
		{
			Assert.That(payload["eventType"], Is.EqualTo("automation_triggered"));
			Assert.That(payload["entityId"], Is.EqualTo("automation.morning"));
			Assert.That(payload["data"], Is.InstanceOf<string>());
			Assert.That((string)payload["data"]!, Does.Contain("automation.morning"));
		});
	}

	[Test]
	public void Event_answers_an_empty_entity_id_when_the_data_carries_none()
	{
		using var document = JsonDocument.Parse("""{ "source": "manual" }""");

		var payload = HomeAssistantEventPayload.Event("logbook_entry", document.RootElement);

		Assert.That(payload["entityId"], Is.EqualTo(string.Empty));
	}

	[Test]
	public void Event_tolerates_data_that_is_undefined()
	{
		var payload = HomeAssistantEventPayload.Event("streamerbot_started", default);

		Assert.Multiple(() =>
		{
			Assert.That(payload["entityId"], Is.EqualTo(string.Empty));
			Assert.That(payload["data"], Is.EqualTo(string.Empty));
		});
	}

	private static HomeAssistantEntityState State(string entityId, string state, string attributesJson = "{}")
	{
		using var document = JsonDocument.Parse(attributesJson);
		return new HomeAssistantEntityState(entityId, state, document.RootElement.Clone());
	}
}
