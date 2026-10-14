using System.Reflection;
using System.Text.RegularExpressions;
using MacroDeckHost.Integrations.Obs;
using MacroDeckHost.Widgets.Weather;

namespace MacroDeckHost.Tests.UnitTests;

[TestFixture]
internal sealed partial class ShippedSvgArtworkTests
{
	// An integration's Icons/animated folder holds animated artwork its dialogs draw, held to the widget
	// artwork rule; every other SVG of an integration must keep its colours without CSS.
	public static IEnumerable<TestCaseData> IntegrationIcons()
	{
		return EmbeddedSvgs(typeof(ObsIntegration).Assembly, name => !IsAnimatedArtwork(name));
	}

	public static IEnumerable<TestCaseData> WidgetArtwork()
	{
		return EmbeddedSvgs(typeof(WeatherWidgetUiProvider).Assembly, _ => true)
			.Concat(EmbeddedSvgs(typeof(ObsIntegration).Assembly, IsAnimatedArtwork));
	}

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
		var css = StyleBlock()
			.Matches(svg)
			.Select(style => Keyframes().Replace(style.Groups[1].Value, string.Empty))
			.Concat(StyleAttribute().Matches(svg).Select(style => style.Groups["css"].Value));

		var properties = css
			.SelectMany(block => Declaration().Matches(block))
			.Select(declaration => VendorPrefix().Replace(declaration.Groups[1].Value, string.Empty))
			.Distinct();

		Assert.That(properties.Where(p
				=> !p.StartsWith("animation-", StringComparison.Ordinal) && p != "transform-origin"),
			Is.Empty);
	}

	private static bool IsAnimatedArtwork(string resourceName)
	{
		return resourceName.Contains(".Icons.animated.", StringComparison.Ordinal);
	}

	private static IEnumerable<TestCaseData> EmbeddedSvgs(Assembly assembly, Func<string, bool> include)
	{
		foreach (var name in assembly.GetManifestResourceNames()
			.Where(n => n.EndsWith(".svg", StringComparison.Ordinal) && include(n)))
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
