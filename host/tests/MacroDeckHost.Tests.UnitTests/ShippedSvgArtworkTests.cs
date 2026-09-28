using System.Reflection;
using System.Text.RegularExpressions;
using MacroDeckHost.Integrations.Obs;
using MacroDeckHost.Widgets.Weather;

namespace MacroDeckHost.Tests.UnitTests;

[TestFixture]
internal sealed partial class ShippedSvgArtworkTests
{
	public static IEnumerable<TestCaseData> IntegrationIcons() => EmbeddedSvgs(typeof(ObsIntegration).Assembly);

	public static IEnumerable<TestCaseData> WidgetArtwork() => EmbeddedSvgs(typeof(WeatherWidgetUiProvider).Assembly);

	[TestCaseSource(nameof(IntegrationIcons))]
	public void An_integration_icon_keeps_its_colours_in_readers_that_ignore_css(string svg)
	{
		Assert.Multiple(() =>
		{
			Assert.That(svg, Does.Not.Contain("<style"));
			Assert.That(StyleAttribute().Matches(svg).Select(style => style.Value), Is.Empty);
		});
	}

	[TestCaseSource(nameof(WidgetArtwork))]
	public void Widget_artwork_only_animates_through_css(string svg)
	{
		var css = StyleBlock().Matches(svg).Select(style => Keyframes().Replace(style.Groups[1].Value, string.Empty))
			.Concat(StyleAttribute().Matches(svg).Select(style => style.Groups["css"].Value));

		var properties = css
			.SelectMany(block => Declaration().Matches(block))
			.Select(declaration => VendorPrefix().Replace(declaration.Groups[1].Value, string.Empty))
			.Distinct();

		Assert.That(properties.Where(p => !p.StartsWith("animation-", StringComparison.Ordinal) && p != "transform-origin"),
			Is.Empty);
	}

	private static IEnumerable<TestCaseData> EmbeddedSvgs(Assembly assembly)
	{
		foreach (var name in assembly.GetManifestResourceNames().Where(n => n.EndsWith(".svg", StringComparison.Ordinal)))
		{
			using var stream = assembly.GetManifestResourceStream(name)!;
			using var reader = new StreamReader(stream);

			yield return new TestCaseData(reader.ReadToEnd()).SetArgDisplayNames(name);
		}
	}

	[GeneratedRegex("<style[^>]*>(.*?)</style>", RegexOptions.Singleline)]
	private static partial Regex StyleBlock();

	[GeneratedRegex("""\sstyle=(?:"(?<css>[^"]*)"|'(?<css>[^']*)')""")]
	private static partial Regex StyleAttribute();

	[GeneratedRegex(@"@(?:-[a-z]+-)?keyframes[^{]*\{(?:[^{}]*\{[^{}]*\})*[^{}]*\}")]
	private static partial Regex Keyframes();

	[GeneratedRegex(@"([a-z-]+)\s*:[^;{}]*(?:;|(?=\})|$)")]
	private static partial Regex Declaration();

	[GeneratedRegex("^-(?:webkit|moz|ms|o)-")]
	private static partial Regex VendorPrefix();
}
