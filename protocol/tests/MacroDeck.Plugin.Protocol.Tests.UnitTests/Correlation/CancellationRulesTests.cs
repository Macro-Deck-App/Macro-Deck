using MacroDeck.Plugin.Protocol.Correlation;

namespace MacroDeck.Plugin.Protocol.Tests.UnitTests.Correlation;

[TestFixture]
public class CancellationRulesTests
{
	[Test]
	public void Cancelling_an_unknown_correlation_is_a_no_op_not_an_error()
	{
		var outcome = CancellationRules.Resolve(isKnownCorrelation: false, alreadyReplied: false);

		Assert.That(outcome, Is.EqualTo(CancellationOutcome.NoOp));
	}

	[Test]
	public void Cancelling_a_known_correlation_that_has_not_replied_emits_a_cancelled_result()
	{
		var outcome = CancellationRules.Resolve(isKnownCorrelation: true, alreadyReplied: false);

		Assert.That(outcome, Is.EqualTo(CancellationOutcome.EmitCancelledResult));
	}

	[Test]
	public void Cancelling_a_correlation_that_already_replied_is_a_no_op()
	{
		var outcome = CancellationRules.Resolve(isKnownCorrelation: true, alreadyReplied: true);

		Assert.That(outcome, Is.EqualTo(CancellationOutcome.NoOp));
	}

	[Test]
	public void An_unknown_correlation_is_a_no_op_regardless_of_already_replied()
	{
		var outcome = CancellationRules.Resolve(isKnownCorrelation: false, alreadyReplied: true);

		Assert.That(outcome, Is.EqualTo(CancellationOutcome.NoOp));
	}
}
