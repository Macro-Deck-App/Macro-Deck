using MacroDeck.Plugin.Protocol.Correlation;
using MacroDeck.Plugin.Protocol.Envelope;

namespace MacroDeck.Plugin.Protocol.Tests.UnitTests.Correlation;

[TestFixture]
public class CorrelationRulesTests
{
	private static readonly string[] _replyTypes =
	[
		MessageTypes.CapabilityResult, MessageTypes.CapabilityDeclareAck, MessageTypes.AssetAck,
	];

	[TestCaseSource(nameof(_replyTypes))]
	public void A_reply_type_without_a_correlation_id_is_malformed(string type)
	{
		var outcome = CorrelationRules.Resolve(type,
			hasCorrelationId: false,
			isKnownCorrelation: false,
			wasTimedOut: false);

		Assert.That(outcome, Is.EqualTo(CorrelationOutcome.MalformedEnvelope));
	}

	[Test]
	public void A_non_reply_type_without_a_correlation_id_is_accepted()
	{
		var outcome = CorrelationRules.Resolve(MessageTypes.EventPublish,
			hasCorrelationId: false,
			isKnownCorrelation: false,
			wasTimedOut: false);

		Assert.That(outcome, Is.EqualTo(CorrelationOutcome.Accept));
	}

	[Test]
	public void An_unrecognised_correlation_is_reported_as_correlation_unknown_never_fatal()
	{
		var outcome = CorrelationRules.Resolve(MessageTypes.CapabilityResult,
			hasCorrelationId: true,
			isKnownCorrelation: false,
			wasTimedOut: false);

		Assert.That(outcome, Is.EqualTo(CorrelationOutcome.CorrelationUnknown));
	}

	[Test]
	public void A_late_reply_for_a_timed_out_correlation_is_dropped_silently_not_reported_as_correlation_unknown()
	{
		var outcome = CorrelationRules.Resolve(MessageTypes.CapabilityResult,
			hasCorrelationId: true,
			isKnownCorrelation: true,
			wasTimedOut: true);

		Assert.That(outcome, Is.EqualTo(CorrelationOutcome.Drop));
	}

	[Test]
	public void A_timed_out_correlation_is_dropped_even_when_it_was_never_known()
	{
		// wasTimedOut is checked before isKnownCorrelation, deliberately: a correlation that timed
		// out is functionally the same as one that no longer exists, and both must drop silently.
		var outcome = CorrelationRules.Resolve(MessageTypes.CapabilityResult,
			hasCorrelationId: true,
			isKnownCorrelation: false,
			wasTimedOut: true);

		Assert.That(outcome, Is.EqualTo(CorrelationOutcome.Drop));
	}

	[Test]
	public void A_known_correlation_that_has_not_timed_out_is_accepted()
	{
		var outcome = CorrelationRules.Resolve(MessageTypes.CapabilityResult,
			hasCorrelationId: true,
			isKnownCorrelation: true,
			wasTimedOut: false);

		Assert.That(outcome, Is.EqualTo(CorrelationOutcome.Accept));
	}
}
