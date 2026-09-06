namespace MacroDeck.Sdk.Tests.UnitTests;

[TestFixture]
public class MacroDeckIntegrationAttributeTests
{
	/// <summary>
	/// The default has to be every platform: almost every integration is cross-platform, and the ones
	/// with per-platform backends still want to be discovered everywhere so they can report what they
	/// cannot do. Getting this default wrong would silently unship integrations.
	/// </summary>
	[Test]
	public void An_integration_runs_everywhere_unless_it_says_otherwise()
	{
		var attribute = new MacroDeckIntegrationAttribute();

		Assert.Multiple(() =>
		{
			Assert.That(attribute.Platforms, Is.EqualTo(MacroDeckPlatform.All));
			Assert.That(attribute.RunsHere(), Is.True);
		});
	}

	/// <summary>
	/// Opting out of being on by default has to be explicit: an integration that works out of the box
	/// would be invisible to a user who never opens the integrations page.
	/// </summary>
	[Test]
	public void An_integration_is_enabled_by_default_unless_it_says_otherwise()
	{
		Assert.That(new MacroDeckIntegrationAttribute().EnabledByDefault, Is.True);
		Assert.That(new MacroDeckIntegrationAttribute { EnabledByDefault = false }.EnabledByDefault, Is.False);
	}

	[Test]
	public void The_current_platform_is_one_of_the_three()
		=> Assert.That(MacroDeckIntegrationAttribute.Current,
			Is.AnyOf(MacroDeckPlatform.Windows, MacroDeckPlatform.MacOS, MacroDeckPlatform.Linux));

	[Test]
	public void An_integration_named_for_this_platform_runs_here()
	{
		var attribute = new MacroDeckIntegrationAttribute
		{
			Platforms = MacroDeckIntegrationAttribute.Current
		};

		Assert.That(attribute.RunsHere(), Is.True);
	}

	[Test]
	public void An_integration_named_for_the_other_platforms_does_not()
	{
		var attribute = new MacroDeckIntegrationAttribute
		{
			Platforms = MacroDeckPlatform.All & ~MacroDeckIntegrationAttribute.Current
		};

		Assert.That(attribute.RunsHere(), Is.False);
	}

	/// <summary>A set of two is still a set: naming two platforms includes both, and excludes the third.</summary>
	[Test]
	public void Platforms_combine()
	{
		var windowsAndLinux = new MacroDeckIntegrationAttribute
		{
			Platforms = MacroDeckPlatform.Windows | MacroDeckPlatform.Linux
		};

		Assert.That(windowsAndLinux.RunsHere(),
			Is.EqualTo(MacroDeckIntegrationAttribute.Current != MacroDeckPlatform.MacOS));
	}
}
