using MacroDeckHost.Application.Plugins.Trust;

namespace MacroDeckHost.Tests.UnitTests.Plugins.Trust;

[TestFixture]
internal sealed class PluginTrustGateTests
{
	[Test]
	public void A_trusted_record_launches_when_the_certificate_id_still_matches()
	{
		var decision = PluginTrustGate.Evaluate(PluginTrustVerdict.Trusted,
			"cert-1",
			PluginTrustResult.Of(PluginTrustVerdict.Trusted, "cert-1"),
			baselineExists: true);

		Assert.That(decision.Permitted, Is.True);
	}

	[TestCase(PluginTrustVerdict.Unsigned)]
	[TestCase(PluginTrustVerdict.ContentMismatch)]
	[TestCase(PluginTrustVerdict.Revoked)]
	public void A_trusted_record_refuses_anything_else_including_unsigned(PluginTrustVerdict onDiskVerdict)
	{
		var decision = PluginTrustGate.Evaluate(PluginTrustVerdict.Trusted,
			"cert-1",
			PluginTrustResult.Of(onDiskVerdict),
			baselineExists: true);

		Assert.That(decision.Permitted, Is.False);
	}

	[Test]
	public void A_trusted_record_refuses_a_different_certificate_id_even_when_the_verdict_is_still_Trusted()
	{
		var decision = PluginTrustGate.Evaluate(PluginTrustVerdict.Trusted,
			"cert-1",
			PluginTrustResult.Of(PluginTrustVerdict.Trusted, "cert-2"),
			baselineExists: true);

		Assert.Multiple(() =>
		{
			Assert.That(decision.Permitted, Is.False);

			// The refusal reason must not claim the plugin "now verifies as Trusted" when its on-disk
			// verdict genuinely is Trusted - only the certificate id changed.
			Assert.That(decision.RefusalReason, Does.Not.Contain("now verifies as Trusted"));
		});
	}

	[Test]
	public void An_unsigned_record_launches_when_still_unsigned()
	{
		var decision = PluginTrustGate.Evaluate(PluginTrustVerdict.Unsigned,
			null,
			PluginTrustResult.Of(PluginTrustVerdict.Unsigned),
			baselineExists: true);

		Assert.That(decision.Permitted, Is.True);
		Assert.That(decision.Action, Is.EqualTo(PluginTrustGateAction.None));
	}

	[Test]
	public void An_unsigned_record_refuses_a_content_mismatch()
	{
		var decision = PluginTrustGate.Evaluate(PluginTrustVerdict.Unsigned,
			null,
			PluginTrustResult.Of(PluginTrustVerdict.ContentMismatch),
			baselineExists: true);

		Assert.That(decision.Permitted, Is.False);
	}

	[Test]
	public void An_unsigned_record_launches_and_upgrades_when_the_artifact_now_verifies_as_trusted()
	{
		var decision = PluginTrustGate.Evaluate(PluginTrustVerdict.Unsigned,
			null,
			PluginTrustResult.Of(PluginTrustVerdict.Trusted, "cert-1"),
			baselineExists: true);

		Assert.That(decision.Permitted, Is.True);
		Assert.That(decision.Action, Is.EqualTo(PluginTrustGateAction.Upgrade));
	}

	[TestCase(PluginTrustVerdict.Trusted)]
	[TestCase(PluginTrustVerdict.Unsigned)]
	public void A_missing_record_before_the_baseline_launches_and_backfills(PluginTrustVerdict onDiskVerdict)
	{
		var decision = PluginTrustGate.Evaluate(admittedVerdict: null,
			admittedCertificateId: null,
			PluginTrustResult.Of(onDiskVerdict, onDiskVerdict == PluginTrustVerdict.Trusted ? "cert-1" : null),
			baselineExists: false);

		Assert.That(decision.Permitted, Is.True);
		Assert.That(decision.Action, Is.EqualTo(PluginTrustGateAction.Backfill));
	}

	[Test]
	public void A_missing_record_after_the_baseline_is_anomalous_and_is_refused()
	{
		var decision = PluginTrustGate.Evaluate(admittedVerdict: null,
			admittedCertificateId: null,
			PluginTrustResult.Of(PluginTrustVerdict.Trusted, "cert-1"),
			baselineExists: true);

		Assert.That(decision.Permitted, Is.False);
	}

	[TestCase(PluginTrustVerdict.Revoked)]
	[TestCase(PluginTrustVerdict.VerificationUnavailable)]
	public void Any_record_refuses_a_revoked_or_unverifiable_artifact(PluginTrustVerdict onDiskVerdict)
	{
		var trustedRecordDecision = PluginTrustGate.Evaluate(PluginTrustVerdict.Trusted,
			"cert-1",
			PluginTrustResult.Of(onDiskVerdict),
			baselineExists: true);
		var unsignedRecordDecision = PluginTrustGate.Evaluate(PluginTrustVerdict.Unsigned,
			null,
			PluginTrustResult.Of(onDiskVerdict),
			baselineExists: true);

		Assert.Multiple(() =>
		{
			Assert.That(trustedRecordDecision.Permitted, Is.False);
			Assert.That(unsignedRecordDecision.Permitted, Is.False);
		});
	}
}
