namespace MacroDeck.Plugin.Testing.Conformance;

/// <summary>
/// Whether a check's failure blocks <see cref="ConformanceReport.Conformant" />. See that property's own
/// remarks for the exact rule: only a <see cref="Required" /> check's <see cref="ConformanceOutcome.Failed" />
/// outcome blocks it - a <see cref="Recommended" /> failure is still reported and still counted, but does
/// not.
/// </summary>
public enum ConformanceRequirement
{
	/// <summary>A failure blocks <see cref="ConformanceReport.Conformant" />.</summary>
	Required,

	/// <summary>
	/// A failure is reported but never blocks <see cref="ConformanceReport.Conformant" /> - used for a check
	/// whose assertion needs cooperation no protocol rule guarantees a conforming plugin provides (e.g. an
	/// action slow enough to observe a deadline against), so a subject that simply has nothing suitable to
	/// invoke is not penalised for it.
	/// </summary>
	Recommended
}
