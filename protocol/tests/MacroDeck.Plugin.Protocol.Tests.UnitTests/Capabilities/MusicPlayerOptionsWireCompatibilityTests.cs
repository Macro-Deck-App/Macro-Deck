using System.Text.Json;
using MacroDeck.Plugin.Protocol.Capabilities.Actions;
using MacroDeck.Plugin.Protocol.Capabilities.MusicPlayer;
using MacroDeck.Plugin.Protocol.Serialization;

namespace MacroDeck.Plugin.Protocol.Tests.UnitTests.Capabilities;

[TestFixture]
public class MusicPlayerOptionsWireCompatibilityTests
{
	[Test]
	public void An_instance_from_a_plugin_that_predates_options_declares_none()
	{
		const string json = """{"id":"account-1","displayName":"Spotify (alice)","hasCatalog":true,"hasDevices":false}""";

		var dto = JsonSerializer.Deserialize<MusicPlayerInstanceDto>(json, PluginProtocolJson.Options)!;

		Assert.That(dto.Options, Is.Empty);
	}

	[Test]
	public void Declared_options_round_trip()
	{
		var dto = new MusicPlayerInstanceDto
		{
			Id = "any-app",
			DisplayName = "Any app",
			Options = [new ActionParameterDto { Name = "cycleSeconds", Type = "Number" }]
		};

		var actual = JsonSerializer.Deserialize<MusicPlayerInstanceDto>(
			JsonSerializer.Serialize(dto, PluginProtocolJson.Options),
			PluginProtocolJson.Options)!;

		Assert.That(actual.Options.Single().Name, Is.EqualTo("cycleSeconds"));
	}

	[Test]
	public void A_state_request_reads_the_same_for_a_plugin_that_predates_options()
	{
		var request = new MusicPlayerStateArguments
		{
			InstanceId = "any-app",
			Options = new Dictionary<string, JsonElement> { ["cycleSeconds"] = JsonSerializer.SerializeToElement(10) }
		};

		var json = JsonSerializer.Serialize(request, PluginProtocolJson.Options);
		var asOldPlugin = JsonSerializer.Deserialize<MusicPlayerInstanceArguments>(json, PluginProtocolJson.Options)!;
		var asNewPlugin = JsonSerializer.Deserialize<MusicPlayerStateArguments>(json, PluginProtocolJson.Options)!;

		Assert.Multiple(() =>
		{
			Assert.That(asOldPlugin.InstanceId, Is.EqualTo("any-app"));
			Assert.That(asNewPlugin.Options!["cycleSeconds"].GetInt32(), Is.EqualTo(10));
		});
	}

	[Test]
	public void A_state_request_from_a_host_that_predates_options_means_the_plain_instance()
	{
		var json = JsonSerializer.Serialize(new MusicPlayerInstanceArguments { InstanceId = "any-app" },
			PluginProtocolJson.Options);

		var arguments = JsonSerializer.Deserialize<MusicPlayerStateArguments>(json, PluginProtocolJson.Options)!;

		Assert.That(arguments.Options, Is.Null);
	}

	[Test]
	public void Option_names_cross_the_wire_unchanged()
	{
		var request = new MusicPlayerArtworkArguments
		{
			InstanceId = "any-app",
			ArtworkId = "cover",
			Options = new Dictionary<string, JsonElement> { ["Cycle_Seconds"] = JsonSerializer.SerializeToElement(10) }
		};

		var json = JsonSerializer.Serialize(request, PluginProtocolJson.Options);

		Assert.That(json, Does.Contain("\"Cycle_Seconds\""));
	}
}
