namespace MacroDeck.Plugin.Testing.Conformance;

/// <summary>What one conformance check concluded about the subject.</summary>
public enum ConformanceOutcome
{
	/// <summary>The subject satisfied the check.</summary>
	Passed,

	/// <summary>The subject violated the check. See <see cref="ConformanceCheckResult.Expected" /> and <see cref="ConformanceCheckResult.Actual" />.</summary>
	Failed,

	/// <summary>
	/// A precondition in <see cref="IConformanceCheck.Requires" /> was unmet, or the check decided at run
	/// time it does not apply to this subject. See <see cref="ConformanceCheckResult.SkipReason" />. Never
	/// blocks <see cref="ConformanceReport.Conformant" />, regardless of <see cref="ConformanceRequirement" />.
	/// </summary>
	Skipped,

	/// <summary>
	/// The check could not reach a verdict for a reason outside the subject's control - the suite's own
	/// infrastructure, not the plugin under test. Never blocks <see cref="ConformanceReport.Conformant" />.
	/// </summary>
	Inconclusive
}
