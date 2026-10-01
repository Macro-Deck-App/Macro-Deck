using MacroDeck.Plugin.Cli.Rendering;
using MacroDeck.Plugin.Protocol.Capabilities.Ui;

namespace MacroDeck.Plugin.Cli.Tests.UnitTests;

[TestFixture]
public class PreviewFileNamesTests
{
	[Test]
	public void A_scenario_name_becomes_a_lowercase_file_name()
	{
		var names = PreviewFileNames.BaseNames([Preview("a", "Clock", "Below Freezing!")]);

		Assert.That(names["a"], Is.EqualTo("below-freezing"));
	}

	[Test]
	public void Scenarios_that_share_a_name_are_told_apart_by_their_view()
	{
		var names = PreviewFileNames.BaseNames([Preview("a", "Clock", "Default"), Preview("b", "Weather", "Default"), Preview("c", "Clock", "Tinted")]);

		Assert.That(names, Is.EqualTo(new Dictionary<string, string>
		{
			["a"] = "clock-default",
			["b"] = "weather-default",
			["c"] = "tinted"
		}));
	}

	[Test]
	public void A_name_that_still_collides_gets_a_number()
	{
		var names = PreviewFileNames.BaseNames([Preview("a", "Clock", "Default"), Preview("b", "Clock", "Default")]);

		Assert.That(names.Values, Is.Unique);
	}

	private static UiPreviewDescriptorDto Preview(string id, string view, string scenario)
		=> new() { Id = id, View = view, Scenario = scenario, Profile = "widget" };
}
