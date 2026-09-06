using MacroDeck.Plugin.Protocol.Compatibility;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeck.Sdk.Deprecation;

namespace MacroDeckHost.Application.Plugins.Compatibility;

public static class PluginCompatibilityEvaluator
{
	private const string ProtocolSubject = "protocol";

	public static PluginCompatibilityReport Evaluate(PluginCompatibilityEvaluation evaluation)
	{
		var usageSource = Classify(evaluation);
		var findings = new List<CompatibilityFinding>();

		findings.AddRange(NegotiationFindings(evaluation));
		findings.AddRange(DeprecationFindings(evaluation, usageSource));

		if (findings.Count > ProtocolLimits.MaxCompatibilityFindings)
		{
			findings = [.. findings.Take(ProtocolLimits.MaxCompatibilityFindings)];
		}

		return new PluginCompatibilityReport
		{
			State = State(evaluation, findings),
			UsageSource = usageSource,
			NegotiatedProtocolVersion = evaluation.NegotiatedProtocolVersion ?? 0,
			SdkVersion = evaluation.Sdk?.SdkVersion,
			UsageTruncated = evaluation.Sdk?.Truncated ?? false,
			Findings = findings
		};
	}

	public static string Classify(PluginCompatibilityEvaluation evaluation) => evaluation.Sdk switch
	{
		{ DeprecatedApis: not null } => CompatibilityFindingSources.Confirmed,
		not null => CompatibilityFindingSources.Inferred,
		_ => CompatibilityFindingSources.Unknown
	};

	private static IEnumerable<CompatibilityFinding> NegotiationFindings(PluginCompatibilityEvaluation evaluation)
	{
		if (evaluation.NegotiatedProtocolVersion is null)
		{
			yield return new CompatibilityFinding
			{
				DiagnosticId = CompatibilityDiagnosticIds.ProtocolUnsupported,
				Source = CompatibilityFindingSources.Negotiated,
				Severity = CompatibilitySeverities.Error,
				Subject = ProtocolSubject,
				Guidance = $"This plugin speaks no protocol version this Macro Deck supports " +
					$"({ProtocolVersions.Minimum}-{ProtocolVersions.Current}). Update the plugin, or run a " +
					"Macro Deck version it was built for."
			};

			yield break;
		}

		foreach (var capability in evaluation.Capabilities.Where(capability => !capability.Accepted))
		{
			yield return new CompatibilityFinding
			{
				DiagnosticId = CompatibilityDiagnosticIds.CapabilityRejected,
				Source = CompatibilityFindingSources.Negotiated,
				Severity = CompatibilitySeverities.Warning,
				Subject = capability.Kind,
				Guidance = $"This capability was not accepted, so the plugin runs without it. " +
					$"{capability.RejectionReason ?? "No reason was given."} Updating the plugin usually " +
					"resolves this."
			};
		}
	}

	private static IEnumerable<CompatibilityFinding> DeprecationFindings(PluginCompatibilityEvaluation evaluation,
		string usageSource)
	{
		if (usageSource == CompatibilityFindingSources.Confirmed)
		{
			foreach (var apiId in evaluation.Sdk!.DeprecatedApis!)
			{
				yield return SdkDeprecations.TryGet(apiId, out var known)
					? Confirmed(known)
					: UnknownApi(apiId);
			}

			yield break;
		}

		if (usageSource != CompatibilityFindingSources.Inferred)
		{
			yield break;
		}

		// Inference, and worded as such. Everything deprecated at or before the SDK the plugin reports is
		// something it *could* be using; the host has no way to narrow that down, and saying otherwise
		// would be the exact false confirmation issue #418 forbids.
		foreach (var deprecation in SdkDeprecations.Active.Where(deprecation
			=> IsAtOrBelow(deprecation.DeprecatedIn, evaluation.Sdk!.SdkVersion)))
		{
			yield return new CompatibilityFinding
			{
				DiagnosticId = CompatibilityDiagnosticIds.PossiblyDeprecatedApi,
				Source = CompatibilityFindingSources.Inferred,
				Severity = CompatibilitySeverities.Info,
				Subject = deprecation.DisplayName,
				Guidance = $"This plugin was built against SDK {evaluation.Sdk!.SdkVersion} and may use " +
					$"this API, which is deprecated. {deprecation.Guidance}",
				DeprecatedIn = deprecation.DeprecatedIn,
				RemovedIn = deprecation.RemovedIn,
				Replacement = deprecation.Replacement,
				MigrationUrl = deprecation.MigrationUrl
			};
		}
	}

	private static CompatibilityFinding Confirmed(SdkDeprecation deprecation) => new()
	{
		DiagnosticId = IsAtOrBelow(deprecation.RemovedIn, HostSdkVersion.Current)
			? CompatibilityDiagnosticIds.RemovedApi
			: CompatibilityDiagnosticIds.DeprecatedApi,
		Source = CompatibilityFindingSources.Confirmed,
		Severity = IsAtOrBelow(deprecation.RemovedIn, HostSdkVersion.Current)
			? CompatibilitySeverities.Error
			: CompatibilitySeverities.Warning,
		Subject = deprecation.DisplayName,
		Guidance = deprecation.Guidance,
		DeprecatedIn = deprecation.DeprecatedIn,
		RemovedIn = deprecation.RemovedIn,
		Replacement = deprecation.Replacement,
		MigrationUrl = deprecation.MigrationUrl
	};

	private static CompatibilityFinding UnknownApi(string apiId) => new()
	{
		DiagnosticId = CompatibilityDiagnosticIds.DeprecatedApi,
		Source = CompatibilityFindingSources.Unknown,
		Severity = CompatibilitySeverities.Info,
		Subject = apiId,
		Guidance = "This plugin reports using an API this Macro Deck version does not know about, which " +
			"usually means it was built against a newer SDK. Updating Macro Deck will describe it."
	};

	private static string State(PluginCompatibilityEvaluation evaluation, IReadOnlyList<CompatibilityFinding> findings)
	{
		var candidates = new List<string> { PluginCompatibilityStates.Compatible };

		if (evaluation.NegotiatedProtocolVersion is null)
		{
			candidates.Add(PluginCompatibilityStates.Incompatible);
		}
		else if (evaluation.Capabilities.Any(capability => !capability.Accepted))
		{
			// Every declared capability being rejected is still only partial: the session itself works,
			// which is what separates this from a plugin that cannot connect at all.
			candidates.Add(PluginCompatibilityStates.PartiallyIncompatible);
		}

		if (findings.Any(finding => finding.Source == CompatibilityFindingSources.Confirmed &&
			finding.DiagnosticId == CompatibilityDiagnosticIds.RemovedApi))
		{
			candidates.Add(PluginCompatibilityStates.UpdateRequired);
		}
		else if (findings.Any(finding => finding.Source == CompatibilityFindingSources.Confirmed))
		{
			candidates.Add(PluginCompatibilityStates.DeprecatedApis);
		}

		if (findings.Any(finding => finding.Source == CompatibilityFindingSources.Inferred))
		{
			candidates.Add(PluginCompatibilityStates.UpdateRecommended);
		}

		return candidates.OrderByDescending(PluginCompatibilityStates.Rank).First();
	}

	private static bool IsAtOrBelow(string? version, Version ceiling)
		=> Version.TryParse(version, out var parsed) && parsed <= ceiling;

	private static bool IsAtOrBelow(string? version, string? ceiling)
		=> Version.TryParse(ceiling, out var parsedCeiling) && IsAtOrBelow(version, parsedCeiling);
}
