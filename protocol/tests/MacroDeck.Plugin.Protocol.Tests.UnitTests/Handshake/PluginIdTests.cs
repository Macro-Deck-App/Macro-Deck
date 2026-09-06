using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Sdk.Identity;

namespace MacroDeck.Plugin.Protocol.Tests.UnitTests.Handshake;

/// <summary>
/// <see cref="PluginId" /> delegates to <see cref="MacroDeckId" /> rather than restating the
/// reverse-domain rule - these tests pin the delegation, not the regex itself.
/// </summary>
[TestFixture]
public class PluginIdTests
{
	[TestCase("app.macro-deck.spotify")]
	[TestCase("com.suchbyte.test-plugin")]
	public void Reverse_domain_owner_ids_are_valid(string ownerId)
		=> Assert.That(PluginId.IsValid(ownerId), Is.True);

	[TestCase("time")]
	[TestCase("App.Macro-Deck.Spotify")]
	[TestCase("a b")]
	[TestCase("app..spotify")]
	[TestCase("")]
	public void Malformed_or_single_segment_ids_are_invalid(string ownerId)
		=> Assert.That(PluginId.IsValid(ownerId), Is.False);

	[Test]
	public void Null_is_invalid()
		=> Assert.That(PluginId.IsValid(null), Is.False);

	[Test]
	public void Max_length_matches_macro_deck_ids_max_owner_id_length()
		=> Assert.That(PluginId.MaxLength, Is.EqualTo(MacroDeckId.MaxOwnerIdLength));

	[Test]
	public void Try_validate_reports_an_error_for_an_invalid_id()
	{
		var isValid = PluginId.TryValidate("Not Valid", out var error);

		Assert.Multiple(() =>
		{
			Assert.That(isValid, Is.False);
			Assert.That(error, Is.Not.Null.And.Not.Empty);
		});
	}

	[Test]
	public void Try_validate_reports_no_error_for_a_valid_id()
	{
		var isValid = PluginId.TryValidate("app.macro-deck.spotify", out var error);

		Assert.Multiple(() =>
		{
			Assert.That(isValid, Is.True);
			Assert.That(error, Is.Null);
		});
	}

	[Test]
	public void Try_validate_agrees_with_is_valid()
	{
		const string ownerId = "app.macro-deck.spotify";

		Assert.That(PluginId.TryValidate(ownerId, out _), Is.EqualTo(PluginId.IsValid(ownerId)));
	}
}
