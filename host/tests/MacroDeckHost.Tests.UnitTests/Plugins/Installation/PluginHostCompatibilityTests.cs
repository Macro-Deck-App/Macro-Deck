using MacroDeckHost.Application.Plugins.Installation;

namespace MacroDeckHost.Tests.UnitTests.Plugins.Installation;

[TestFixture]
internal sealed class PluginHostCompatibilityTests
{
	[TestCase(">=3.0.0-beta.12", "3.0.0-beta.11")]
	[TestCase(">=3.0.0-beta.12,<3.0.0", "3.0.0-beta.11")]
	[TestCase("=3.0.0-beta.12", "3.0.0-beta.11")]
	[TestCase(">=3.0.0", "3.0.0-beta.11")]
	[TestCase(">=3.1.0,<4.0.0", "3.0.2")]
	[TestCase(">=4.2.0", "3.0.0")]
	public void A_range_only_a_later_Macro_Deck_satisfies_asks_for_a_newer_Macro_Deck(string range, string host)
	{
		Assert.That(PluginHostCompatibility.ClassifyMacroDeckRange(range, host),
			Is.EqualTo(PluginIncompatibility.HostTooOld));
	}

	[TestCase("<3.0.0", "3.0.0")]
	[TestCase(">=2.0.0,<2.5.0", "3.0.0-beta.11")]
	[TestCase("=2.0.0", "3.0.0")]
	public void A_range_no_later_Macro_Deck_satisfies_is_plainly_incompatible(string range, string host)
	{
		Assert.That(PluginHostCompatibility.ClassifyMacroDeckRange(range, host),
			Is.EqualTo(PluginIncompatibility.HostTooNew));
	}

	[TestCase("^3.0.0", "3.0.0")]
	[TestCase(">=3.0.0", "not-a-version")]
	public void A_range_or_host_version_that_cannot_be_read_has_no_direction(string range, string host)
	{
		Assert.That(PluginHostCompatibility.ClassifyMacroDeckRange(range, host),
			Is.EqualTo(PluginIncompatibility.Unknown));
	}
}
