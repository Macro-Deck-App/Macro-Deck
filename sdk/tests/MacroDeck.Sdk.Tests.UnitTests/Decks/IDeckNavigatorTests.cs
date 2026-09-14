using MacroDeck.Sdk.Decks;

namespace MacroDeck.Sdk.Tests.UnitTests.Decks;

[TestFixture]
public class IDeckNavigatorTests
{
	[Test]
	public void An_implementer_that_predates_client_positions_reports_no_clients()
	{
		IDeckNavigator navigator = new Minimal();

		Assert.That(navigator.GetClients(), Is.Empty);
	}

	[Test]
	public void An_implementer_that_predates_client_positions_accepts_and_ignores_ClientChanged_handlers()
	{
		IDeckNavigator navigator = new Minimal();
		EventHandler<DeckClientChangedEventArgs> handler = (_, _) => { };

		Assert.DoesNotThrow(() =>
		{
			navigator.ClientChanged += handler;
			navigator.ClientChanged -= handler;
		});
	}

	private sealed class Minimal : IDeckNavigator
	{
		public Task ChangeFolderAsync(string folderId,
			string? originClientId = null,
			CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task ChangeProfileAsync(string profileId,
			string? originClientId = null,
			CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task GoToParentAsync(string? originClientId = null, CancellationToken cancellationToken = default)
			=> Task.CompletedTask;

		public Task GoBackAsync(string? originClientId = null, CancellationToken cancellationToken = default)
			=> Task.CompletedTask;

		public IReadOnlyList<DeckFolder> GetFolders() => [];

		public IReadOnlyList<DeckProfile> GetProfiles() => [];
	}
}
