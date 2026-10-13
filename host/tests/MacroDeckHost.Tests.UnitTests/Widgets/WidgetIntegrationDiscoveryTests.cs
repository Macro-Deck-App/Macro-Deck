using MacroDeckHost.Infrastructure.SystemIntegrations;
using MacroDeckHost.Infrastructure.SystemIntegrations.Widgets;

namespace MacroDeckHost.Tests.UnitTests.Widgets;

[TestFixture]
internal sealed class WidgetIntegrationDiscoveryTests
{
	[Test]
	public void The_host_discovery_finds_the_widget_integration()
	{
		var discovered = BuiltInIntegrations.Discover(Serilog.Log.Logger);

		var widget = discovered.OfType<WidgetIntegration>().SingleOrDefault();
		Assert.That(widget, Is.Not.Null, "the Widget integration was not discovered");
		Assert.Multiple(() =>
		{
			Assert.That(widget!.Id, Is.EqualTo(WidgetIntegration.IntegrationId));
			Assert.That(widget.Actions, Is.Not.Empty);
		});
	}
}
