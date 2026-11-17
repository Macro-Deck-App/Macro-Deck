using System.Text.Json;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Sdk.Events;

namespace MacroDeck.Plugin.Protocol.Tests.UnitTests.Callbacks;

[TestFixture]
public class EventBindingWireTests
{
	[Test]
	public void A_resolved_value_travels_as_resolvedValue_and_reaches_the_sdk_binding()
	{
		var json = JsonSerializer.Serialize(new EventBindingDto
			{
				EventId = "scene-changed",
				Parameters = new Dictionary<string, EventBindingValueDto>(StringComparer.Ordinal)
				{
					["scene"] = new()
					{
						Value = JsonSerializer.SerializeToElement(new Dictionary<string, string>
							{ ["$var"] = "scene" }),
						ResolvedValue = JsonSerializer.SerializeToElement("Intro")
					}
				}
			},
			PluginProtocolJson.Options);

		var binding = JsonSerializer.Deserialize<EventBindingDto>(json, PluginProtocolJson.Options)!.ToBinding();

		Assert.Multiple(() =>
		{
			Assert.That(json, Does.Contain("\"resolvedValue\":\"Intro\""));
			Assert.That(binding.Parameters["scene"].ResolvedValue!.Value.GetString(), Is.EqualTo("Intro"));
			Assert.That(binding.Parameters["scene"].Value!.Value.GetProperty("$var").GetString(), Is.EqualTo("scene"));
		});
	}

	[Test]
	public void A_literal_value_is_sent_without_resolvedValue()
	{
		var json = JsonSerializer.Serialize(new EventBindingValueDto
			{
				Value = JsonSerializer.SerializeToElement("Intro"),
				Operator = "=="
			},
			PluginProtocolJson.Options);

		Assert.That(json, Does.Not.Contain("resolvedValue"));
	}

	[Test]
	public void A_push_from_a_host_that_predates_resolved_values_still_reads()
	{
		const string json = """{"eventId":"scene-changed","parameters":{"scene":{"value":"Intro","operator":"=="}}}""";

		var binding = JsonSerializer.Deserialize<EventBindingDto>(json, PluginProtocolJson.Options)!.ToBinding();

		Assert.Multiple(() =>
		{
			Assert.That(binding.Parameters["scene"].Value!.Value.GetString(), Is.EqualTo("Intro"));
			Assert.That(binding.Parameters["scene"].ResolvedValue, Is.Null);
		});
	}

	[Test]
	public void A_plugin_written_against_the_two_value_binding_keeps_building_and_deconstructing_it()
	{
		var value = JsonSerializer.SerializeToElement("Intro");

		var (stored, comparison) = new EventBindingValue(value, "==");

		Assert.Multiple(() =>
		{
			Assert.That(stored!.Value.GetString(), Is.EqualTo("Intro"));
			Assert.That(comparison, Is.EqualTo("=="));
		});
	}

	[Test]
	public void The_resolved_value_feature_keeps_its_wire_name()
	{
		Assert.Multiple(() =>
		{
			Assert.That(HostApiFeatures.EventBindingResolvedValue, Is.EqualTo("event-bindings.resolved-value"));
			Assert.That(HostApiFeatures.All, Does.Contain(HostApiFeatures.EventBindingResolvedValue));
		});
	}
}
