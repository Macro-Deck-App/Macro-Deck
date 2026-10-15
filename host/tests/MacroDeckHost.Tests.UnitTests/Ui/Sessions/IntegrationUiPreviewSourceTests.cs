using MacroDeckHost.Integrations;
using MacroDeckHost.Widgets.DeveloperPreviews;

namespace MacroDeckHost.Tests.UnitTests.Ui.Sessions;

[TestFixture]
internal sealed class IntegrationUiPreviewSourceTests
{
	private const string Assembly = "MacroDeckHost.Integrations";

	private readonly IntegrationUiPreviewSource _source = new(typeof(IntegrationDiscovery).Assembly);

	[TestCase("MacroDeckHost.Integrations.Weather.Widgets.WeatherDetailsViewPreviews.ClearDay")]
	[TestCase("MacroDeckHost.Integrations.Weather.Widgets.WeatherDetailsViewPreviews.Unavailable")]
	[TestCase("MacroDeckHost.Integrations.Jellyfin.Widgets.JellyfinSessionsViewPreviews.Several")]
	[TestCase("MacroDeckHost.Integrations.Twitch.Widgets.TwitchStatsWidgetViewPreviews.Overview")]
	public void A_built_in_integrations_scenario_is_listed_for_developer_tools(string member)
	{
		Assert.That(_source.Previews.Select(preview => preview.Id), Does.Contain($"{Assembly}:{member}"));
	}

	[Test]
	public void Built_in_integrations_declare_no_malformed_scenario()
	{
		Assert.That(_source.Skipped, Is.Empty);
	}
}
