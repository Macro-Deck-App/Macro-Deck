using MacroDeck.Plugin.Protocol.Versioning;

namespace MacroDeck.Plugin.Protocol.Tests.UnitTests.Versioning;

[TestFixture]
public class ProtocolVersionNegotiatorTests
{
	[TestCaseSource(typeof(ProtocolVersions), nameof(ProtocolVersions.Supported))]
	public void An_exact_single_version_range_negotiates_to_that_version(int version)
	{
		var outcome = ProtocolVersionNegotiator.Negotiate(new ProtocolVersionRange
			{ Minimum = version, Maximum = version });

		Assert.Multiple(() =>
		{
			Assert.That(outcome.Succeeded, Is.True);
			Assert.That(outcome.NegotiatedVersion, Is.EqualTo(version));
		});
	}

	[Test]
	public void A_client_range_entirely_below_the_minimum_fails_and_echoes_the_host_range()
	{
		var outcome = ProtocolVersionNegotiator.Negotiate(new ProtocolVersionRange
			{ Minimum = ProtocolVersions.Minimum - 5, Maximum = ProtocolVersions.Minimum - 1 });

		Assert.Multiple(() =>
		{
			Assert.That(outcome.Succeeded, Is.False);
			Assert.That(outcome.NegotiatedVersion, Is.Null);
			Assert.That(outcome.HostRange.Minimum, Is.EqualTo(ProtocolVersions.Minimum));
			Assert.That(outcome.HostRange.Maximum, Is.EqualTo(ProtocolVersions.Current));
		});
	}

	[Test]
	public void A_client_range_entirely_above_current_fails()
	{
		var outcome = ProtocolVersionNegotiator.Negotiate(new ProtocolVersionRange
			{ Minimum = ProtocolVersions.Current + 1, Maximum = ProtocolVersions.Current + 5 });

		Assert.That(outcome.Succeeded, Is.False);
	}

	[Test]
	public void A_wide_client_range_negotiates_to_the_hosts_current_version()
	{
		var outcome = ProtocolVersionNegotiator.Negotiate(new ProtocolVersionRange
			{ Minimum = ProtocolVersions.Minimum, Maximum = ProtocolVersions.Current + 100 });

		Assert.Multiple(() =>
		{
			Assert.That(outcome.Succeeded, Is.True);
			Assert.That(outcome.NegotiatedVersion, Is.EqualTo(ProtocolVersions.Current));
		});
	}

	[Test]
	public void A_failed_negotiation_always_carries_the_hosts_own_range_so_the_plugin_can_act_on_it()
	{
		var outcome = ProtocolVersionNegotiator.Negotiate(new ProtocolVersionRange
			{ Minimum = ProtocolVersions.Current + 10, Maximum = ProtocolVersions.Current + 10 });

		Assert.That(outcome.HostRange, Is.Not.Null);
	}
}
