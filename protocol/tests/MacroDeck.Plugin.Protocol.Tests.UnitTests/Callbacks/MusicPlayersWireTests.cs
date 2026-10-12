using System.Text.Json;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Sdk.MusicPlayer;

namespace MacroDeck.Plugin.Protocol.Tests.UnitTests.Callbacks;

[TestFixture]
public class MusicPlayersWireTests
{
	[Test]
	public void A_pushed_player_travels_under_its_frozen_wire_names()
	{
		var json = JsonSerializer.SerializeToElement(new MusicPlayersStateDto
			{
				Revision = 4,
				Instances =
				[
					MusicPlayerWire.ToDto(new MusicPlayerInstanceSnapshot
					{
						Id = "net.example.jukebox::default",
						ProviderId = "net.example.jukebox",
						ProviderName = "Jukebox",
						DisplayName = "Jukebox (living room)"
					})
				]
			},
			PluginProtocolJson.Options);

		var instance = json.GetProperty("instances")[0];
		Assert.Multiple(() =>
		{
			Assert.That(json.GetProperty("revision").GetInt64(), Is.EqualTo(4));
			Assert.That(instance.GetProperty("id").GetString(), Is.EqualTo("net.example.jukebox::default"));
			Assert.That(instance.GetProperty("providerId").GetString(), Is.EqualTo("net.example.jukebox"));
			Assert.That(instance.GetProperty("providerName").GetString(), Is.EqualTo("Jukebox"));
			Assert.That(instance.GetProperty("displayName").GetString(), Is.EqualTo("Jukebox (living room)"));
		});
	}

	[Test]
	public void The_album_colour_call_travels_under_its_frozen_wire_names()
	{
		var arguments = JsonSerializer.SerializeToElement(
			new MusicPlayerAlbumColorArguments { InstanceId = "spotify::alice" },
			PluginProtocolJson.Options);
		var result = JsonSerializer.SerializeToElement(new MusicPlayerAlbumColorResult { Color = "#102030" },
			PluginProtocolJson.Options);

		Assert.Multiple(() =>
		{
			Assert.That(arguments.GetProperty("instanceId").GetString(), Is.EqualTo("spotify::alice"));
			Assert.That(result.GetProperty("color").GetString(), Is.EqualTo("#102030"));
		});
	}

	[Test]
	public void A_push_from_a_newer_host_with_fields_this_version_does_not_know_still_reads()
	{
		var state = JsonSerializer.Deserialize<MusicPlayersStateDto>(
			"""{"revision":2,"instances":[{"id":"a::b","providerId":"a","displayName":"B","hasArtwork":true}],"future":1}""",
			PluginProtocolJson.Options)!;

		var snapshot = MusicPlayerWire.ToSnapshot(state.Instances.Single());

		Assert.Multiple(() =>
		{
			Assert.That(snapshot.Id, Is.EqualTo("a::b"));
			Assert.That(snapshot.DisplayName, Is.EqualTo("B"));
			Assert.That(snapshot.ProviderName.IsEmpty, Is.True);
		});
	}
}
