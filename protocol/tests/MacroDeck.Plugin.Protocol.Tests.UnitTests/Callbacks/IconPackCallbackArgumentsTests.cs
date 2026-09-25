using System.Text.Json;
using MacroDeck.Plugin.Protocol.Callbacks.IconPacks;
using MacroDeck.Plugin.Protocol.Serialization;

namespace MacroDeck.Plugin.Protocol.Tests.UnitTests.Callbacks;

[TestFixture]
public class IconPackCallbackArgumentsTests
{
	[Test]
	public void A_sync_carries_every_declared_pack_under_the_documented_names()
	{
		var arguments = new IconPackSyncArguments
		{
			Packs = [new BundledIconPackDeclarationDto { Key = "logos", ContentHash = "abc", ByteLength = 12 }]
		};

		var json = JsonSerializer.Serialize(arguments, PluginProtocolJson.Options);

		Assert.That(json, Is.EqualTo("""{"packs":[{"key":"logos","contentHash":"abc","byteLength":12}]}"""));
	}

	[Test]
	public void A_sync_result_asking_for_uploads_reads_without_a_changed_flag()
	{
		var result = JsonSerializer.Deserialize<IconPackSyncResult>("""{"uploadRequired":["abc"]}""",
			PluginProtocolJson.Options);

		Assert.Multiple(() =>
		{
			Assert.That(result!.UploadRequired, Is.EqualTo((string[])["abc"]));
			Assert.That(result.Changed, Is.False);
		});
	}

	[Test]
	public void A_completed_sync_result_reads_without_an_upload_list()
	{
		var result = JsonSerializer.Deserialize<IconPackSyncResult>("""{"changed":true}""", PluginProtocolJson.Options);

		Assert.Multiple(() =>
		{
			Assert.That(result!.UploadRequired, Is.Empty);
			Assert.That(result.Changed, Is.True);
		});
	}

	[Test]
	public void An_icon_resource_lookup_names_the_pack_key_and_the_icon_name()
	{
		var json = JsonSerializer.Serialize(new GetIconResourceArguments { Key = "logos", Name = "spotify" },
			PluginProtocolJson.Options);

		Assert.That(json, Is.EqualTo("""{"key":"logos","name":"spotify"}"""));
	}
}
