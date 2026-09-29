using MacroDeckHost.Widgets;

namespace MacroDeckHost.Tests.UnitTests.Widgets;

[TestFixture]
public class WidgetColorTests
{
	[TestCase("transparent", "transparent")]
	[TestCase(" Transparent ", "transparent")]
	[TestCase("#1E88E5", "#1e88e5")]
	[TestCase("#abc", "#aabbcc")]
	[TestCase("rgba(10, 20, 30, 0.5)", "#0a141e")]
	[TestCase("navy", "#000080")]
	[TestCase("var(--color-accent)", null)]
	[TestCase("", null)]
	[TestCase(null, null)]
	public void A_background_is_an_opaque_hex_colour_or_transparent(string? raw, string? expected)
	{
		Assert.That(WidgetColor.NormalizeBackground(raw), Is.EqualTo(expected));
	}

	[TestCase("transparent")]
	[TestCase("#11223344")]
	public void Other_colours_stay_opaque_so_transparent_reaches_backgrounds_only(string raw)
	{
		Assert.That(WidgetColor.Normalize(raw), Is.Not.EqualTo("transparent"));
	}
}
