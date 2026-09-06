using MacroDeckHost.Integrations;
using MacroDeckHost.Integrations.Delegation;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeck.Sdk;

namespace MacroDeckHost.Tests.UnitTests.Delegation;

[TestFixture]
internal sealed class DelegateIntegrationTests
{
	private DelegateIntegration _integration = null!;

	[SetUp]
	public void SetUp() => _integration = new DelegateIntegration();

	[TearDown]
	public void TearDown() => _integration.Dispose();

	[Test]
	public void The_integration_is_identified()
	{
		Assert.Multiple(() =>
		{
			Assert.That(_integration.Id, Is.EqualTo("app.macro-deck.delegate"));
			Assert.That(TestLocalization.Resolve(_integration.Name), Is.EqualTo("Macro Deck Delegate"));
			Assert.That(typeof(DelegateIntegration).GetCustomAttributes(typeof(MacroDeckIntegrationAttribute), false),
				Is.Not.Empty);
		});
	}

	[Test]
	public void The_host_discovery_finds_the_integration_and_can_construct_it()
	{
		var discovered = IntegrationDiscovery.DiscoverIntegrations(Serilog.Log.Logger);

		var delegateIntegration = discovered.OfType<DelegateIntegration>().SingleOrDefault();

		Assert.That(delegateIntegration, Is.Not.Null, "the Delegate integration was not discovered");
	}

	[Test]
	public void The_brand_icon_is_a_png()
	{
		var icon = _integration.GetIcon();

		Assert.Multiple(() =>
		{
			Assert.That(_integration.IconMimeType, Is.EqualTo("image/png"));
			Assert.That(icon, Is.Not.Empty);

			Assert.That(icon[..8], Is.EqualTo(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }));
		});
	}
}
