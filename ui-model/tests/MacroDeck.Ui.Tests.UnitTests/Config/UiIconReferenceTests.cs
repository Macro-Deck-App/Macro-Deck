using MacroDeck.Ui.Config;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;

namespace MacroDeck.Ui.Tests.UnitTests.Config;

[TestFixture]
public class UiIconReferenceTests
{
	[Test]
	public void A_plugin_icon_names_the_pack_key_and_the_icon_name()
		=> Assert.That(UiIconReference.PluginIcon("logos", "spotify"),
			Is.EqualTo(new UiIconReference("plugin-icon", "logos/spotify")));

	[TestCase("")]
	[TestCase("Logos")]
	[TestCase("-logos")]
	[TestCase("logos-")]
	[TestCase("lo/gos")]
	[TestCase("a1234567890123456789012345678901234567890123456789012345678901234")]
	public void A_key_that_is_not_a_bundled_pack_key_is_refused(string key)
		=> Assert.That(() => UiIconReference.PluginIcon(key, "spotify"),
			Throws.ArgumentException.With.Property(nameof(ArgumentException.ParamName)).EqualTo("key"));

	[TestCase("")]
	[TestCase(" ")]
	[TestCase("brand/spotify")]
	public void A_blank_name_or_one_with_a_slash_is_refused(string name)
		=> Assert.That(() => UiIconReference.PluginIcon("logos", name),
			Throws.ArgumentException.With.Property(nameof(ArgumentException.ParamName)).EqualTo("name"));

	[Test]
	public void A_plugin_icon_default_travels_as_an_ordinary_typed_icon_reference()
	{
		var view = new UiView(new UiSurface { Kind = UiSurfaceKinds.Config, SessionMode = UiSessionModes.Exclusive },
			new UiIconReferenceInput { Key = "icon", DefaultValue = UiIconReference.PluginIcon("logos", "spotify") });

		var node = view.Tree.Root.Id == "icon"
			? view.Tree.Root
			: view.Tree.Root.Children.Single(child => child.Id.EndsWith("icon", StringComparison.Ordinal));

		Assert.That(node.Properties["defaultValue"].GetRawText(),
			Is.EqualTo("""{"type":"plugin-icon","reference":"logos/spotify"}"""));
	}
}
