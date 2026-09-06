using MacroDeck.Plugin.Protocol.Compatibility;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeckHost.Application.Plugins.Compatibility;
using MacroDeck.Sdk.Deprecation;

namespace MacroDeckHost.Tests.UnitTests.Plugins.Compatibility;

[TestFixture]
public class PluginCompatibilityEvaluatorTests
{
	private const string OldSdkVersion = "1.0.0";

	private const string UnknownApiId = "M:MacroDeck.Sdk.Future.NotYetInvented.Method(System.String)";

	private static readonly string[] _noDeprecatedApis = [];

	private static readonly string[] _oneUnknownDeprecatedApi = [UnknownApiId];

	private static readonly string[] _confirmedStates =
	[
		PluginCompatibilityStates.DeprecatedApis, PluginCompatibilityStates.UpdateRequired,
	];

	private static PluginCompatibilityEvaluation Evaluation(
		int? protocolVersion = 1,
		IReadOnlyList<CapabilityNegotiationResult>? capabilities = null,
		PluginSdkUsage? sdk = null)
		=> new()
		{
			PluginId = "com.example.plugin",
			DisplayName = "Example",
			NegotiatedProtocolVersion = protocolVersion,
			Capabilities = capabilities ?? [],
			Sdk = sdk
		};

	private static PluginSdkUsage Sdk(
		string version = OldSdkVersion,
		IReadOnlyList<string>? deprecatedApis = null,
		bool truncated = false)
		=> new() { SdkVersion = version, DeprecatedApis = deprecatedApis, Truncated = truncated };


	[Test]
	public void A_plugin_that_reports_no_sdk_block_is_classified_unknown()
	{
		var evaluation = Evaluation();

		Assert.Multiple(() =>
		{
			Assert.That(PluginCompatibilityEvaluator.Classify(evaluation),
				Is.EqualTo(CompatibilityFindingSources.Unknown));
			Assert.That(PluginCompatibilityEvaluator.Evaluate(evaluation).UsageSource,
				Is.EqualTo(CompatibilityFindingSources.Unknown));
		});
	}

	[Test]
	public void An_sdk_version_without_a_usage_manifest_is_classified_inferred()
	{
		// Null DeprecatedApis means the generator never ran, so the host has a version number and nothing
		// else. Anything stronger than "inferred" here is the exact misreport issue #418 forbids.
		var evaluation = Evaluation(sdk: Sdk(deprecatedApis: null));

		Assert.Multiple(() =>
		{
			Assert.That(PluginCompatibilityEvaluator.Classify(evaluation),
				Is.EqualTo(CompatibilityFindingSources.Inferred));
			Assert.That(PluginCompatibilityEvaluator.Evaluate(evaluation).UsageSource,
				Is.EqualTo(CompatibilityFindingSources.Inferred));
		});
	}

	[Test]
	public void An_empty_usage_manifest_is_classified_confirmed()
	{
		var evaluation = Evaluation(sdk: Sdk(deprecatedApis: _noDeprecatedApis));

		Assert.Multiple(() =>
		{
			Assert.That(PluginCompatibilityEvaluator.Classify(evaluation),
				Is.EqualTo(CompatibilityFindingSources.Confirmed));
			Assert.That(PluginCompatibilityEvaluator.Evaluate(evaluation).UsageSource,
				Is.EqualTo(CompatibilityFindingSources.Confirmed));
		});
	}

	[Test]
	public void A_populated_usage_manifest_is_classified_confirmed()
	{
		var evaluation = Evaluation(sdk: Sdk(deprecatedApis: _oneUnknownDeprecatedApi));

		Assert.That(PluginCompatibilityEvaluator.Classify(evaluation),
			Is.EqualTo(CompatibilityFindingSources.Confirmed));
	}

	[Test]
	public void A_capability_negotiation_outcome_is_sourced_as_negotiated()
	{
		var evaluation = Evaluation(capabilities:
		[
			CapabilityNegotiationResult.Accept(CapabilityKinds.Actions, 1),
			CapabilityNegotiationResult.Reject(CapabilityKinds.Variables, "no version in common")
		]);

		var report = PluginCompatibilityEvaluator.Evaluate(evaluation);
		var finding = report.Findings.SingleOrDefault(f => f.Subject.Contains(CapabilityKinds.Variables,
			StringComparison.Ordinal));

		Assert.Multiple(() =>
		{
			Assert.That(finding, Is.Not.Null, "the rejected capability must be reported");
			Assert.That(finding!.Source, Is.EqualTo(CompatibilityFindingSources.Negotiated));
			Assert.That(report.Findings.Any(f => f.Subject.Contains(CapabilityKinds.Actions,
					StringComparison.Ordinal)),
				Is.False,
				"an accepted capability is not a finding");
		});
	}


	[Test]
	public void An_old_sdk_without_a_usage_manifest_never_produces_a_confirmed_finding()
	{
		var evaluation = Evaluation(sdk: Sdk(version: OldSdkVersion, deprecatedApis: null));

		var report = PluginCompatibilityEvaluator.Evaluate(evaluation);

		Assert.Multiple(() =>
		{
			Assert.That(report.UsageSource, Is.EqualTo(CompatibilityFindingSources.Inferred));
			Assert.That(report.Findings.Select(f => f.Source),
				Has.None.EqualTo(CompatibilityFindingSources.Confirmed));
			Assert.That(report.State,
				Is.Not.AnyOf(_confirmedStates),
				"both of these states are defined as confirmed use of a deprecated or removed API");
			Assert.That(report.SdkVersion, Is.EqualTo(OldSdkVersion));
		});
	}

	[Test]
	public void An_empty_usage_manifest_confirms_that_nothing_deprecated_is_used()
	{
		// The other half of the same rule: a plugin that proved it uses nothing deprecated must come out
		// clean, not merely "unknown", and must not collect a deprecation finding from its version.
		var evaluation = Evaluation(capabilities: [CapabilityNegotiationResult.Accept(CapabilityKinds.Actions, 1)],
			sdk: Sdk(version: OldSdkVersion, deprecatedApis: _noDeprecatedApis));

		var report = PluginCompatibilityEvaluator.Evaluate(evaluation);

		Assert.Multiple(() =>
		{
			Assert.That(report.UsageSource, Is.EqualTo(CompatibilityFindingSources.Confirmed));
			Assert.That(report.State, Is.EqualTo(PluginCompatibilityStates.Compatible));
			Assert.That(report.Findings, Is.Empty);
		});
	}

	[Test]
	public void An_api_id_the_host_does_not_recognise_is_still_reported_but_not_as_confirmed()
	{
		// A plugin built against a newer SDK is telling the truth about an API this host has never heard
		// of. Dropping the row would hide a real problem; calling it confirmed would state something the
		// host cannot know, since it has no metadata for the id at all.
		var evaluation = Evaluation(sdk: Sdk(deprecatedApis: _oneUnknownDeprecatedApi));

		var report = PluginCompatibilityEvaluator.Evaluate(evaluation);
		var finding = report.Findings.SingleOrDefault(f =>
			f.Subject.Contains(UnknownApiId, StringComparison.Ordinal));

		Assert.Multiple(() =>
		{
			Assert.That(finding, Is.Not.Null, "an unrecognised api id must still be reported");
			Assert.That(finding!.Source, Is.EqualTo(CompatibilityFindingSources.Unknown));
			Assert.That(finding.Guidance, Is.Not.Empty);
			Assert.That(report.State, Is.Not.AnyOf(_confirmedStates));
		});
	}

	[Test]
	public void An_sdk_old_enough_to_predate_a_deprecation_yields_inferred_findings_only()
	{
		if (SdkDeprecations.Active.Count == 0)
		{
			Assert.Ignore("No SDK deprecations to infer from yet.");
		}

		var report = PluginCompatibilityEvaluator.Evaluate(
			Evaluation(sdk: Sdk(version: "999.0.0", deprecatedApis: null)));

		Assert.Multiple(() =>
		{
			Assert.That(report.Findings, Is.Not.Empty);
			Assert.That(report.Findings.Select(f => f.Source),
				Is.All.EqualTo(CompatibilityFindingSources.Inferred));
			Assert.That(report.Findings.Select(f => f.Severity),
				Has.None.EqualTo(CompatibilitySeverities.Error));
			Assert.That(report.State, Is.EqualTo(PluginCompatibilityStates.UpdateRecommended));
		});
	}

	[Test]
	public void A_reported_api_the_host_knows_is_confirmed_and_carries_its_migration_metadata()
	{
		if (SdkDeprecations.Active.Count == 0)
		{
			Assert.Ignore("No SDK deprecations to report usage of yet.");
		}

		var deprecation = SdkDeprecations.Active[0];
		var report = PluginCompatibilityEvaluator.Evaluate(Evaluation(sdk: Sdk(deprecatedApis: [deprecation.ApiId])));

		var finding = report.Findings.Single();

		Assert.Multiple(() =>
		{
			Assert.That(finding.Source, Is.EqualTo(CompatibilityFindingSources.Confirmed));
			Assert.That(finding.DeprecatedIn, Is.EqualTo(deprecation.DeprecatedIn));
			Assert.That(finding.RemovedIn, Is.EqualTo(deprecation.RemovedIn));
			Assert.That(finding.Guidance, Is.Not.Empty);
			Assert.That(report.State, Is.AnyOf(_confirmedStates));
		});
	}


	[Test]
	public void A_fully_negotiated_plugin_with_nothing_reported_is_compatible()
	{
		var evaluation = Evaluation(capabilities:
		[
			CapabilityNegotiationResult.Accept(CapabilityKinds.Actions, 1)
		]);

		var report = PluginCompatibilityEvaluator.Evaluate(evaluation);

		Assert.Multiple(() =>
		{
			Assert.That(report.State, Is.EqualTo(PluginCompatibilityStates.Compatible));
			Assert.That(report.UsageSource, Is.EqualTo(CompatibilityFindingSources.Unknown));
			Assert.That(report.SdkVersion, Is.Null);
		});
	}

	[Test]
	public void A_plugin_with_no_negotiable_protocol_version_is_incompatible()
	{
		var evaluation = Evaluation(protocolVersion: null);

		var report = PluginCompatibilityEvaluator.Evaluate(evaluation);

		Assert.Multiple(() =>
		{
			Assert.That(report.State, Is.EqualTo(PluginCompatibilityStates.Incompatible));
			Assert.That(report.Findings, Is.Not.Empty, "the user must be told why it cannot connect");
			Assert.That(report.Findings.Select(f => f.Source),
				Has.Some.EqualTo(CompatibilityFindingSources.Negotiated));
			Assert.That(report.Findings.Select(f => f.Severity),
				Has.Some.EqualTo(CompatibilitySeverities.Error));
		});
	}

	[Test]
	public void A_rejected_capability_beside_an_accepted_one_is_partially_incompatible()
	{
		var evaluation = Evaluation(capabilities:
		[
			CapabilityNegotiationResult.Accept(CapabilityKinds.Actions, 1),
			CapabilityNegotiationResult.Reject(CapabilityKinds.Variables, "no version in common")
		]);

		Assert.That(PluginCompatibilityEvaluator.Evaluate(evaluation).State,
			Is.EqualTo(PluginCompatibilityStates.PartiallyIncompatible));
	}

	[Test]
	public void A_rejected_capability_outranks_a_merely_inferred_finding()
	{
		var evaluation = Evaluation(capabilities:
			[
				CapabilityNegotiationResult.Reject(CapabilityKinds.Variables, "no version in common")
			],
			sdk: Sdk(version: OldSdkVersion, deprecatedApis: null));

		var report = PluginCompatibilityEvaluator.Evaluate(evaluation);

		Assert.Multiple(() =>
		{
			Assert.That(report.State, Is.EqualTo(PluginCompatibilityStates.PartiallyIncompatible));
			Assert.That(report.UsageSource,
				Is.EqualTo(CompatibilityFindingSources.Inferred),
				"the worst state must not erase the evidence behind the rest of the report");
		});
	}

	[Test]
	public void A_failed_protocol_negotiation_outranks_every_other_finding()
	{
		var evaluation = Evaluation(protocolVersion: null,
			capabilities:
			[
				CapabilityNegotiationResult.Reject(CapabilityKinds.Variables, "no version in common")
			],
			sdk: Sdk(deprecatedApis: _oneUnknownDeprecatedApi));

		Assert.That(PluginCompatibilityEvaluator.Evaluate(evaluation).State,
			Is.EqualTo(PluginCompatibilityStates.Incompatible));
	}

	[Test]
	public void The_reported_state_is_always_one_the_wire_vocabulary_knows()
	{
		// A state the UI cannot rank renders as nothing at all, so an evaluator that invents one is worse
		// than one that reports a lesser state.
		var evaluations = new[]
		{
			Evaluation(),
			Evaluation(protocolVersion: null),
			Evaluation(sdk: Sdk(deprecatedApis: null)),
			Evaluation(sdk: Sdk(deprecatedApis: _noDeprecatedApis)),
			Evaluation(sdk: Sdk(deprecatedApis: _oneUnknownDeprecatedApi)),
			Evaluation(capabilities:
			[
				CapabilityNegotiationResult.Reject(CapabilityKinds.Actions, "no version in common")
			])
		};

		Assert.Multiple(() =>
		{
			foreach (var evaluation in evaluations)
			{
				var report = PluginCompatibilityEvaluator.Evaluate(evaluation);

				Assert.That(PluginCompatibilityStates.IsKnown(report.State),
					Is.True,
					$"unknown state '{report.State}'");
				Assert.That(PluginCompatibilityStates.Rank(report.State), Is.GreaterThanOrEqualTo(0));
				Assert.That(CompatibilityFindingSources.IsKnown(report.UsageSource),
					Is.True,
					$"unknown usage source '{report.UsageSource}'");
			}
		});
	}


	[Test]
	public void Every_finding_carries_actionable_guidance_and_a_known_vocabulary()
	{
		// A finding without guidance is a dead end for the user, and one with an unknown source or
		// severity cannot be rendered honestly by the UI.
		var evaluation = Evaluation(protocolVersion: null,
			capabilities:
			[
				CapabilityNegotiationResult.Reject(CapabilityKinds.Variables, "no version in common"),
				CapabilityNegotiationResult.Reject(CapabilityKinds.Actions, "no version in common")
			],
			sdk: Sdk(deprecatedApis: _oneUnknownDeprecatedApi));

		var report = PluginCompatibilityEvaluator.Evaluate(evaluation);

		Assert.That(report.Findings, Is.Not.Empty);
		Assert.Multiple(() =>
		{
			foreach (var finding in report.Findings)
			{
				Assert.That(finding.Guidance, Is.Not.Empty, $"{finding.DiagnosticId} has no guidance");
				Assert.That(finding.DiagnosticId, Is.Not.Empty);
				Assert.That(finding.Subject, Is.Not.Empty);
				Assert.That(CompatibilityFindingSources.IsKnown(finding.Source),
					Is.True,
					$"unknown source '{finding.Source}'");
				Assert.That(CompatibilitySeverities.IsKnown(finding.Severity),
					Is.True,
					$"unknown severity '{finding.Severity}'");
			}
		});
	}


	[Test]
	public void The_report_echoes_the_negotiated_protocol_version_and_sdk_version()
	{
		var evaluation = Evaluation(protocolVersion: 7, sdk: Sdk(version: "2.3.4", deprecatedApis: null));

		var report = PluginCompatibilityEvaluator.Evaluate(evaluation);

		Assert.Multiple(() =>
		{
			Assert.That(report.NegotiatedProtocolVersion, Is.EqualTo(7));
			Assert.That(report.SdkVersion, Is.EqualTo("2.3.4"));
		});
	}

	[Test]
	public void A_truncated_usage_manifest_is_flagged_so_the_findings_read_as_a_floor()
	{
		var evaluation = Evaluation(sdk: Sdk(deprecatedApis: _oneUnknownDeprecatedApi, truncated: true));

		var report = PluginCompatibilityEvaluator.Evaluate(evaluation);

		Assert.Multiple(() =>
		{
			Assert.That(report.UsageTruncated, Is.True);
			Assert.That(PluginCompatibilityEvaluator
					.Evaluate(Evaluation(sdk: Sdk(deprecatedApis: _oneUnknownDeprecatedApi))).UsageTruncated,
				Is.False);
		});
	}

	[Test]
	public void The_findings_of_one_report_stay_within_the_protocol_limit()
	{
		var capabilities = Enumerable
			.Range(0, (ProtocolLimits.MaxCompatibilityFindings * 2) + 5)
			.Select(i => CapabilityNegotiationResult.Reject($"kind-{i}", "no version in common"))
			.ToList();

		var report = PluginCompatibilityEvaluator.Evaluate(Evaluation(capabilities: capabilities));

		Assert.Multiple(() =>
		{
			Assert.That(report.Findings, Has.Count.LessThanOrEqualTo(ProtocolLimits.MaxCompatibilityFindings));
			Assert.That(report.State,
				Is.EqualTo(PluginCompatibilityStates.PartiallyIncompatible),
				"dropping findings must not change the verdict");
		});
	}
}
