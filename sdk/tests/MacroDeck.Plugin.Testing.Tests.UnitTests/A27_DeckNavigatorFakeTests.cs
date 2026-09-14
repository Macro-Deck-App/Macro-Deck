using MacroDeck.Plugin.Testing.Fakes;
using MacroDeck.Sdk.Decks;

namespace MacroDeck.Plugin.Testing.Tests.UnitTests;

[TestFixture]
public class A27_DeckNavigatorFakeTests
{
	private static DeckClient Client(string clientId, string folderId)
		=> new() { ClientId = clientId, ProfileId = "p1", FolderId = folderId };

	[Test]
	public void Seeded_clients_are_listed_without_raising_ClientChanged()
	{
		var deck = new FakeDeckNavigator();
		var raised = 0;
		deck.ClientChanged += (_, _) => raised++;

		deck.SeedClients(Client("tab-1", "f1"));

		Assert.Multiple(() =>
		{
			Assert.That(deck.GetClients().Single().FolderId, Is.EqualTo("f1"));
			Assert.That(raised, Is.Zero);
		});
	}

	[Test]
	public void Raising_a_move_replaces_the_client_and_reports_its_previous_position()
	{
		var deck = new FakeDeckNavigator();
		deck.SeedClients(Client("tab-1", "f1"), Client("tab-2", "f9"));
		DeckClientChangedEventArgs? seen = null;
		((IDeckNavigator)deck).ClientChanged += (_, change) => seen = change;

		deck.RaiseClientChanged(Client("tab-1", "f2"), "p1", "f1");

		Assert.Multiple(() =>
		{
			Assert.That(deck.GetClients().Select(client => (client.ClientId, client.FolderId)),
				Is.EquivalentTo(new[] { ("tab-1", "f2"), ("tab-2", "f9") }));
			Assert.That(seen!.Client.FolderId, Is.EqualTo("f2"));
			Assert.That(seen.PreviousFolderId, Is.EqualTo("f1"));
			Assert.That(seen.PreviousProfileId, Is.EqualTo("p1"));
		});
	}
}
