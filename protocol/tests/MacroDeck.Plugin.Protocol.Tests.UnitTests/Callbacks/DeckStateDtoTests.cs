using System.Text.Json;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Serialization;

namespace MacroDeck.Plugin.Protocol.Tests.UnitTests.Callbacks;

[TestFixture]
public class DeckStateDtoTests
{
	[Test]
	public void A_deck_push_from_an_older_host_reads_as_no_clients_and_revision_zero()
	{
		var state = JsonSerializer.Deserialize<DeckStateDto>("""{"folders":[],"profiles":[]}""",
			PluginProtocolJson.Options)!;

		Assert.Multiple(() =>
		{
			Assert.That(state.Clients, Is.Empty);
			Assert.That(state.Revision, Is.Zero);
		});
	}

	[Test]
	public void Client_positions_and_the_revision_travel_under_their_frozen_wire_names()
	{
		var json = JsonSerializer.SerializeToElement(new DeckStateDto
			{
				Clients =
				[
					new DeckClientDto { ClientId = "tab-1", DeviceId = "d1", ProfileId = "p1", FolderId = "f1" }
				],
				Revision = 7
			},
			PluginProtocolJson.Options);

		var client = json.GetProperty("clients")[0];
		Assert.Multiple(() =>
		{
			Assert.That(json.GetProperty("revision").GetInt64(), Is.EqualTo(7));
			Assert.That(client.GetProperty("clientId").GetString(), Is.EqualTo("tab-1"));
			Assert.That(client.GetProperty("deviceId").GetString(), Is.EqualTo("d1"));
			Assert.That(client.GetProperty("profileId").GetString(), Is.EqualTo("p1"));
			Assert.That(client.GetProperty("folderId").GetString(), Is.EqualTo("f1"));
		});
	}
}
