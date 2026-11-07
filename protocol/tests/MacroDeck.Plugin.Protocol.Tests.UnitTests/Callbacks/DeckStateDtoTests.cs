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
	public void A_client_from_a_host_that_predates_device_sessions_reads_as_not_a_device_session()
	{
		var state = JsonSerializer.Deserialize<DeckStateDto>(
			"""{"clients":[{"clientId":"device:d1","deviceId":"d1","profileId":"p1","folderId":"f1"}]}""",
			PluginProtocolJson.Options)!;

		Assert.That(state.Clients.Single().IsDeviceSession, Is.False);
	}

	[Test]
	public void A_device_session_travels_under_its_wire_name()
	{
		var json = JsonSerializer.SerializeToElement(
			new DeckClientDto { ClientId = "device:d1", IsDeviceSession = true },
			PluginProtocolJson.Options);

		Assert.That(json.GetProperty("isDeviceSession").GetBoolean(), Is.True);
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
