using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Versioning;

namespace MacroDeck.Plugin.Protocol.Tests.UnitTests.Versioning;

/// <summary>
/// Parameterised over <see cref="ProtocolVersions.Supported" /> rather than hard-coding version 1, so
/// the matrix widens on its own when v2 lands - the issue's "compatibility tests must cover the oldest
/// and newest supported protocol versions" criterion.
/// </summary>
[TestFixture]
public class ProtocolCompatibilityTests
{
	[TestCaseSource(typeof(ProtocolVersions), nameof(ProtocolVersions.Supported))]
	public void For_each_supported_version_an_exact_range_negotiates_to_that_version(int version)
	{
		var outcome = ProtocolVersionNegotiator.Negotiate(new ProtocolVersionRange
			{ Minimum = version, Maximum = version });

		Assert.That(outcome.NegotiatedVersion, Is.EqualTo(version));
	}

	[Test]
	public void The_oldest_to_newest_range_negotiates_to_the_newest_supported_version()
	{
		var oldest = ProtocolVersions.Supported[0];
		var newest = ProtocolVersions.Supported[^1];

		var outcome = ProtocolVersionNegotiator.Negotiate(new ProtocolVersionRange
			{ Minimum = oldest, Maximum = newest });

		Assert.Multiple(() =>
		{
			Assert.That(outcome.Succeeded, Is.True);
			Assert.That(outcome.NegotiatedVersion, Is.EqualTo(newest));
			Assert.That(outcome.NegotiatedVersion, Is.EqualTo(ProtocolVersions.Current));
		});
	}

	[Test]
	public void Supported_is_contiguous_and_ascending()
	{
		Assert.Multiple(() =>
		{
			for (var i = 1; i < ProtocolVersions.Supported.Count; i++)
			{
				Assert.That(ProtocolVersions.Supported[i], Is.EqualTo(ProtocolVersions.Supported[i - 1] + 1));
			}
		});
	}

	[Test]
	public void Supported_starts_at_minimum_and_ends_at_current()
	{
		Assert.Multiple(() =>
		{
			Assert.That(ProtocolVersions.Supported[0], Is.EqualTo(ProtocolVersions.Minimum));
			Assert.That(ProtocolVersions.Supported[^1], Is.EqualTo(ProtocolVersions.Current));
		});
	}

	[Test]
	public void Supported_is_never_empty()
		=> Assert.That(ProtocolVersions.Supported, Is.Not.Empty);

	[Test]
	public void A_message_may_omit_protocol_version()
	{
		// protocolVersion is nullable on the envelope; the compatibility rule that a message may
		// omit it is pinned at the type level here rather than only stated in prose.
		var envelope = new ProtocolEnvelope
			{ Type = MessageTypes.SessionPing, Id = "0f8fad5b-d9cb-469f-a165-70867728950e" };

		Assert.That(envelope.ProtocolVersion, Is.Null);
	}

	[Test]
	public void Capability_versions_negotiate_independently_of_the_session_version()
	{
		var sessionOutcome = ProtocolVersionNegotiator.Negotiate(new ProtocolVersionRange
			{ Minimum = ProtocolVersions.Minimum, Maximum = ProtocolVersions.Current });
		var capabilityOutcome = CapabilityVersionNegotiator.Negotiate(CapabilityKinds.Actions,
			new CapabilityVersionRange { Minimum = ProtocolVersions.Minimum, Maximum = ProtocolVersions.Current });

		Assert.Multiple(() =>
		{
			Assert.That(sessionOutcome.Succeeded, Is.True);
			Assert.That(capabilityOutcome.Accepted, Is.True);
		});
	}
}
