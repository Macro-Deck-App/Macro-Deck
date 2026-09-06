namespace MacroDeck.Plugin.Testing.Conformance;

/// <summary>
/// Something a check needs the subject to have produced before <see cref="IConformanceCheck.RunAsync" />
/// can mean anything. <see cref="ConformanceRunner" /> evaluates every member of
/// <see cref="IConformanceCheck.Requires" /> against what the subject actually produced
/// <strong>before</strong> calling <see cref="IConformanceCheck.RunAsync" /> - an unmet precondition
/// short-circuits straight to <see cref="ConformanceOutcome.Skipped" />, so a check body never has to guard
/// against, say, a null <see cref="ConformanceContext.Session" /> itself.
/// </summary>
public enum ConformancePrecondition
{
	/// <summary>A connected <see cref="ConformanceContext.Session" />.</summary>
	Session,

	/// <summary>A <see cref="ConformanceContext.Manifest" /> - an <see cref="ConformanceSubject.InProcess" />
	/// or <see cref="ConformanceSubject.Artifact" /> subject always has one; an
	/// <see cref="ConformanceSubject.Executable" /> subject never does, since nothing resolves its
	/// manifest.json for it.</summary>
	Manifest,

	/// <summary>The subject's <see cref="ConformanceContext.Plugin" /> is an <see cref="ExternalPlugin" /> - a real, separate process.</summary>
	ExternalProcess,

	/// <summary>A <see cref="ConformanceContext.Clock" /> - only an <see cref="ConformanceSubject.InProcess" /> subject is given one.</summary>
	Clock
}
