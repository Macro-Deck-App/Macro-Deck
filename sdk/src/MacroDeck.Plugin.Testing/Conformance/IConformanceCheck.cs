namespace MacroDeck.Plugin.Testing.Conformance;

/// <summary>
/// One conformance rule, framework-agnostic by design: no NUnit, xUnit, MSTest or assertion-library type
/// appears anywhere in this contract or in <see cref="ConformanceCheckResult" />, so the exact same check
/// runs unmodified under any of them - a thin adapter (this repository's own is
/// <c>MacroDeck.Plugin.Testing.Tests.ConformanceTests</c>) turns each one into a test.
///
/// <para>
/// <see cref="Id" /> is a stable public contract: a third party pins it in CI to allow-list or suppress a
/// specific check, so an id, once shipped, is never reused for a different rule and never changes meaning -
/// only <see cref="Title" /> and a check's internal behaviour may still evolve.
/// </para>
/// </summary>
public interface IConformanceCheck
{
	/// <summary>
	/// This check's stable id, of the form <c>MDC&lt;nn&gt;&lt;nn&gt;</c> - a two-digit category number
	/// matching <see cref="Category" /> followed by a two-digit sequence number unique within it. Numbered
	/// per category specifically so adding a new check never renumbers an existing one.
	/// </summary>
	string Id { get; }

	/// <summary>A short, human-readable statement of what this check verifies.</summary>
	string Title { get; }

	/// <summary>Which of the eight areas this check belongs to.</summary>
	ConformanceCategory Category { get; }

	/// <summary>Whether a failure blocks <see cref="ConformanceReport.Conformant" />.</summary>
	ConformanceRequirement Requirement { get; }

	/// <summary>
	/// What the subject must have produced before this check can mean anything.
	/// <see cref="ConformanceRunner" /> evaluates this before calling <see cref="RunAsync" /> - an unmet
	/// precondition short-circuits to <see cref="ConformanceOutcome.Skipped" /> without ever invoking this
	/// method, so <see cref="RunAsync" /> may assume every listed precondition already holds.
	/// </summary>
	IReadOnlyList<ConformancePrecondition> Requires { get; }

	/// <summary>
	/// Runs the check against <paramref name="context" />. Every precondition in <see cref="Requires" /> is
	/// already satisfied by the time this is called. May still return <see cref="ConformanceOutcome.Skipped" />
	/// or <see cref="ConformanceOutcome.Inconclusive" /> for a condition <see cref="Requires" /> has no
	/// vocabulary for (e.g. "the subject declares at least one action") - but must never return
	/// <see cref="ConformanceOutcome.Passed" /> for something it did not actually verify.
	/// </summary>
	Task<ConformanceCheckResult> RunAsync(ConformanceContext context, CancellationToken cancellationToken);
}
