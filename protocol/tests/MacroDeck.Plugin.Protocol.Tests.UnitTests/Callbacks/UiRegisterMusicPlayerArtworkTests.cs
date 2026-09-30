using System.Text.Json;
using MacroDeck.Plugin.Protocol.Callbacks.Ui;
using MacroDeck.Plugin.Protocol.Serialization;

namespace MacroDeck.Plugin.Protocol.Tests.UnitTests.Callbacks;

[TestFixture]
public class UiRegisterMusicPlayerArtworkTests
{
	[Test]
	public void A_request_names_the_resource_the_player_instance_and_the_artwork()
	{
		var json = JsonSerializer.Serialize(
			new UiRegisterMusicPlayerArtworkArguments
			{
				Name = "cover", InstanceId = "net.example.jukebox::default", ArtworkId = "cover-1"
			},
			PluginProtocolJson.Options);

		Assert.That(json,
			Is.EqualTo("""{"name":"cover","instanceId":"net.example.jukebox::default","artworkId":"cover-1"}"""));
	}

	[Test]
	public void An_answer_with_a_handle_reads_its_resource()
	{
		var result = JsonSerializer.Deserialize<UiRegisterMusicPlayerArtworkResult>(
			"""{"resource":{"resourceId":"plugin-x.cover","contentHash":"h","mediaType":"image/webp","byteLength":9}}""",
			PluginProtocolJson.Options);

		Assert.Multiple(() =>
		{
			Assert.That(result!.Resource!.ResourceId, Is.EqualTo("plugin-x.cover"));
			Assert.That(result.Resource.ByteLength, Is.EqualTo(9));
		});
	}

	[Test]
	public void An_answer_without_a_handle_reads_as_no_artwork()
	{
		var result = JsonSerializer.Deserialize<UiRegisterMusicPlayerArtworkResult>("{}", PluginProtocolJson.Options);

		Assert.That(result!.Resource, Is.Null);
	}
}
