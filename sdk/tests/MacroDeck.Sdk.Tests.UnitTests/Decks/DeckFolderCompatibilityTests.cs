using MacroDeck.Sdk.Decks;

namespace MacroDeck.Sdk.Tests.UnitTests.Decks;

[TestFixture]
public class DeckFolderCompatibilityTests
{
	[Test]
	public void A_folder_built_the_way_an_older_plugin_builds_it_still_compiles_and_has_no_tree_position()
	{
		var folder = new DeckFolder { Id = "f1", Label = "Home" };

		Assert.Multiple(() =>
		{
			Assert.That(folder.ProfileId, Is.Null);
			Assert.That(folder.ParentId, Is.Null);
			Assert.That(folder.Order, Is.Null);
		});
	}
}
