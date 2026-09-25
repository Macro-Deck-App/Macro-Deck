using MacroDeck.Sdk.Actions;

namespace MacroDeck.Sdk.Tests.UnitTests.Actions;

[TestFixture]
public class ActionIconReferenceTests
{
	[Test]
	public void A_plugin_icon_names_the_pack_key_and_the_icon_name()
	{
		var reference = ActionIconReference.PluginIcon("logos", "spotify");

		Assert.That(reference, Is.EqualTo(new ActionIconReference("plugin-icon", "logos/spotify")));
	}

	[Test]
	public void An_icon_name_keeps_its_spelling()
		=> Assert.That(ActionIconReference.PluginIcon("status-2", "Now Playing").Reference,
			Is.EqualTo("status-2/Now Playing"));

	[Test]
	public void The_existing_icon_pack_reference_is_unchanged()
		=> Assert.That(ActionIconReference.IconPack("0f8fad5b"),
			Is.EqualTo(new ActionIconReference("icon-pack", "0f8fad5b")));

	[TestCase("")]
	[TestCase("Logos")]
	[TestCase("-logos")]
	[TestCase("logos-")]
	[TestCase("lo/gos")]
	[TestCase("lo gos")]
	[TestCase("a1234567890123456789012345678901234567890123456789012345678901234")]
	public void A_key_that_is_not_a_bundled_pack_key_is_refused(string key)
		=> Assert.That(() => ActionIconReference.PluginIcon(key, "spotify"),
			Throws.ArgumentException.With.Property(nameof(ArgumentException.ParamName)).EqualTo("key"));

	[TestCase("")]
	[TestCase("  ")]
	[TestCase("brand/spotify")]
	public void A_blank_name_or_one_with_a_slash_is_refused(string name)
		=> Assert.That(() => ActionIconReference.PluginIcon("logos", name),
			Throws.ArgumentException.With.Property(nameof(ArgumentException.ParamName)).EqualTo("name"));
}
