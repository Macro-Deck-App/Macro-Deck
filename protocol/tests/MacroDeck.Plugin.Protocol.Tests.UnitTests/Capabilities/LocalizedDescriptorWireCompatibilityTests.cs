using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Plugin.Protocol.Capabilities.Actions;
using MacroDeck.Plugin.Protocol.Capabilities.ConfigFlow;
using MacroDeck.Plugin.Protocol.Serialization;

namespace MacroDeck.Plugin.Protocol.Tests.UnitTests.Capabilities;

/// <summary>
/// The descriptor DTOs changed from <c>string</c> to <c>LocalizedText</c> so a plugin's action names and
/// parameter labels can reach the reader's client as references. That is a source break for a plugin
/// author, and these are the assertions that keep it from also being a wire break: descriptors travel
/// plugin to host, so a plugin built against the string shape has to stay readable by this host, and a
/// host that only ever wrote strings has to keep producing byte-identical output.
/// </summary>
[TestFixture]
public class LocalizedDescriptorWireCompatibilityTests
{
	private const string OldDescriptorJson = """
											 {
											 	"localId": "play",
											 	"name": "Play",
											 	"description": "Starts playback",
											 	"parameters": [
											 		{ "name": "device", "type": "String", "label": "Device", "placeholder": "Living room" }
											 	]
											 }
											 """;

	[Test]
	public void A_descriptor_from_a_plugin_built_against_the_string_shape_still_reads()
	{
		var descriptor = JsonSerializer.Deserialize<ActionDescriptorDto>(OldDescriptorJson, PluginProtocolJson.Options);

		Assert.Multiple(() =>
		{
			Assert.That(descriptor!.Name.Literal, Is.EqualTo("Play"));
			Assert.That(descriptor.Name.IsLocalized, Is.False);
			Assert.That(descriptor.Description.Literal, Is.EqualTo("Starts playback"));
			Assert.That(descriptor.Parameters[0].Label?.Literal, Is.EqualTo("Device"));
			Assert.That(descriptor.Parameters[0].Placeholder?.Literal, Is.EqualTo("Living room"));
			Assert.That(descriptor.Parameters[0].Description, Is.Null, "an absent label must not become empty text");
		});
	}

	[Test]
	public void A_literal_descriptor_serializes_to_the_bytes_it_always_did()
	{
		var descriptor = new ActionDescriptorDto
		{
			LocalId = "play",
			Name = "Play",
			Description = "Starts playback",
			Parameters = []
		};

		var json = JsonSerializer.Serialize(descriptor, PluginProtocolJson.Options);

		Assert.Multiple(() =>
		{
			Assert.That(json, Does.Contain("\"name\":\"Play\""));
			Assert.That(json, Does.Not.Contain("$localized"));
		});
	}

	[Test]
	public void A_reference_round_trips_as_a_reference_rather_than_as_resolved_text()
	{
		var key = new LocalizationKey(LocalizationScope.ForPlugin("com.example.spotify"), "Actions.Play");
		var descriptor = new ActionDescriptorDto
		{
			LocalId = "play",
			Name = new LocalizedString(key),
			Description = "Starts playback",
			Parameters = []
		};

		var json = JsonSerializer.Serialize(descriptor, PluginProtocolJson.Options);
		var round = JsonSerializer.Deserialize<ActionDescriptorDto>(json, PluginProtocolJson.Options);

		Assert.Multiple(() =>
		{
			Assert.That(json, Does.Contain("$localized"));
			Assert.That(round!.Name.Localized!.Value.Key, Is.EqualTo(key));
		});
	}

	/// <summary>
	/// <c>DynamicOptionsResultDto.Error</c> is additive: a plugin built before it existed sends no
	/// <c>error</c> property at all, and this host must read that as "no error" rather than as an empty
	/// but present one, so the unchanged options list still drives the unchanged behaviour.
	/// </summary>
	[Test]
	public void A_dynamic_options_result_with_no_error_property_deserializes_with_an_empty_error()
	{
		const string oldResultJson = """
									 {
									 	"options": [{ "value": "device-1", "label": "Device 1" }],
									 	"allowsCustomValue": true,
									 	"cacheSeconds": 30
									 }
									 """;

		var result = JsonSerializer.Deserialize<DynamicOptionsResultDto>(oldResultJson, PluginProtocolJson.Options);

		Assert.Multiple(() =>
		{
			Assert.That(result!.Error, Is.Null);
			Assert.That(result.Options.Single().Value, Is.EqualTo("device-1"));
			Assert.That(result.AllowsCustomValue, Is.True);
		});
	}

	[Test]
	public void A_config_flow_entry_title_stays_a_plain_string()
	{
		// Not an oversight: the host stores it as the configured entry's name, which the user then owns
		// and can rename. A name that changed language under them is a different bug from a stale label.
		Assert.That(typeof(ConfigFlowResultDto).GetProperty(nameof(ConfigFlowResultDto.EntryTitle))!.PropertyType,
			Is.EqualTo(typeof(string)));
	}
}
