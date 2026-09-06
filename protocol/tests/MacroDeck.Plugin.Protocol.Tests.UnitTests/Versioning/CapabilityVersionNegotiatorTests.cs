using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Versioning;

namespace MacroDeck.Plugin.Protocol.Tests.UnitTests.Versioning;

[TestFixture]
public class CapabilityVersionNegotiatorTests
{
	[Test]
	public void A_non_overlapping_range_is_rejected_without_failing_the_session()
	{
		CapabilityNegotiationResult result = null!;
		Assert.DoesNotThrow(() => result = CapabilityVersionNegotiator.Negotiate(CapabilityKinds.Actions,
			new CapabilityVersionRange { Minimum = 100, Maximum = 200 }));

		Assert.Multiple(() =>
		{
			Assert.That(result.Accepted, Is.False);
			Assert.That(result.NegotiatedVersion, Is.Null);
			Assert.That(result.RejectionReason, Is.Not.Null.And.Not.Empty);
		});
	}

	[Test]
	public void An_unknown_capability_kind_is_rejected_rather_than_throwing()
	{
		CapabilityNegotiationResult result = null!;
		Assert.DoesNotThrow(() => result = CapabilityVersionNegotiator.Negotiate("not-a-real-kind",
			new CapabilityVersionRange { Minimum = 1, Maximum = 1 }));

		Assert.Multiple(() =>
		{
			Assert.That(result.Accepted, Is.False);
			Assert.That(result.RejectionReason, Is.Not.Null.And.Not.Empty);
		});
	}

	[TestCaseSource(typeof(CapabilityKinds), nameof(CapabilityKinds.All))]
	public void An_overlapping_range_for_a_known_kind_is_accepted(string kind)
	{
		var result = CapabilityVersionNegotiator.Negotiate(kind,
			new CapabilityVersionRange { Minimum = 1, Maximum = 1 });

		Assert.Multiple(() =>
		{
			Assert.That(result.Accepted, Is.True);
			Assert.That(result.NegotiatedVersion, Is.EqualTo(1));
			Assert.That(result.Kind, Is.EqualTo(kind));
		});
	}

	[Test]
	public void A_wide_client_range_negotiates_to_the_hosts_current_version()
	{
		var result = CapabilityVersionNegotiator.Negotiate(CapabilityKinds.Icons,
			new CapabilityVersionRange { Minimum = ProtocolVersions.Minimum, Maximum = ProtocolVersions.Current + 50 });

		Assert.Multiple(() =>
		{
			Assert.That(result.Accepted, Is.True);
			Assert.That(result.NegotiatedVersion, Is.EqualTo(ProtocolVersions.Current));
		});
	}
}
