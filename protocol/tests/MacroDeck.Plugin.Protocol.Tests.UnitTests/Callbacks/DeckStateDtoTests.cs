using System.Text.Json;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Sdk.Decks;

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

	[Test]
	public void A_folder_from_an_older_host_reads_with_no_tree_position()
	{
		var state = JsonSerializer.Deserialize<DeckStateDto>("""{"folders":[{"id":"f1","label":"Home"}]}""",
			PluginProtocolJson.Options)!;

		var folder = state.Folders.Single();
		Assert.Multiple(() =>
		{
			Assert.That(folder.Id, Is.EqualTo("f1"));
			Assert.That(folder.ProfileId, Is.Null);
			Assert.That(folder.ParentId, Is.Null);
			Assert.That(folder.Order, Is.Null);
		});
	}

	[Test]
	public void A_folders_tree_position_travels_under_its_wire_names()
	{
		var json = JsonSerializer.SerializeToElement(new DeckStateDto
			{
				Folders =
				[
					new DeckFolder { Id = "f2", Label = "Scenes", ProfileId = "p1", ParentId = "f1", Order = 3 }
				]
			},
			PluginProtocolJson.Options);

		var folder = json.GetProperty("folders")[0];
		Assert.Multiple(() =>
		{
			Assert.That(folder.GetProperty("profileId").GetString(), Is.EqualTo("p1"));
			Assert.That(folder.GetProperty("parentId").GetString(), Is.EqualTo("f1"));
			Assert.That(folder.GetProperty("order").GetInt32(), Is.EqualTo(3));
		});
	}

	[Test]
	public void A_top_level_folder_omits_its_parent_on_the_wire()
	{
		var json = JsonSerializer.SerializeToElement(new DeckStateDto
			{
				Folders = [new DeckFolder { Id = "f1", Label = "Home", ProfileId = "p1", Order = 0 }]
			},
			PluginProtocolJson.Options);

		Assert.That(json.GetProperty("folders")[0].TryGetProperty("parentId", out _), Is.False);
	}
}
