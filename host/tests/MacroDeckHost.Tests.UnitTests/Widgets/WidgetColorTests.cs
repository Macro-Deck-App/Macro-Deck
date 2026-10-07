using MacroDeckHost.Widgets;

namespace MacroDeckHost.Tests.UnitTests.Widgets;

[TestFixture]
public class WidgetColorTests
{
	[TestCase("transparent", "transparent")]
	[TestCase(" Transparent ", "transparent")]
	[TestCase("#1E88E5", "#1e88e5")]
	[TestCase("#abc", "#aabbcc")]
	[TestCase("rgba(10, 20, 30, 0.5)", "#0a141e80")]
	[TestCase("#11223344", "#11223344")]
	[TestCase("#112233ff", "#112233")]
	[TestCase("navy", "#000080")]
	[TestCase("var(--color-accent)", null)]
	[TestCase("", null)]
	[TestCase(null, null)]
	public void A_background_is_a_hex_colour_keeping_its_alpha_or_transparent(string? raw, string? expected)
	{
		Assert.That(WidgetColor.NormalizeBackground(raw), Is.EqualTo(expected));
	}

	[TestCase("transparent")]
	[TestCase("#11223344")]
	public void Other_colours_never_read_as_transparent_so_transparent_reaches_backgrounds_only(string raw)
	{
		Assert.That(WidgetColor.Normalize(raw), Is.Not.EqualTo("transparent"));
	}
}
