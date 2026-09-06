namespace MacroDeck.Plugin.Testing.Conformance;

/// <summary>
/// What one check concluded about one subject. Produced only through <see cref="Pass" />,
/// <see cref="Fail" />, <see cref="Skip" /> or <see cref="Inconclusive" /> - each factory guarantees the
/// shape <see cref="ConformanceReportWriter" /> and <see cref="ConformanceReport.Conformant" /> both depend
/// on: a <see cref="ConformanceOutcome.Failed" /> result always carries non-empty <see cref="Expected" />
/// and <see cref="Actual" />, and a <see cref="ConformanceOutcome.Skipped" /> or
/// <see cref="ConformanceOutcome.Inconclusive" /> result always carries a non-empty <see cref="SkipReason" />.
/// </summary>
public sealed record ConformanceCheckResult
{
	private ConformanceCheckResult()
	{
	}

	/// <summary>What the check concluded.</summary>
	public required ConformanceOutcome Outcome { get; init; }

	/// <summary>What a conforming subject was expected to do. Set only by <see cref="Fail" />.</summary>
	public string? Expected { get; init; }

	/// <summary>What the subject actually did. Set only by <see cref="Fail" />.</summary>
	public string? Actual { get; init; }

	/// <summary>Why the check did not reach a verdict. Set only by <see cref="Skip" /> and <see cref="Inconclusive" />.</summary>
	public string? SkipReason { get; init; }

	/// <summary>How long the check took to run. Set by <see cref="ConformanceRunner" /> after the check returns - never by the check itself.</summary>
	public TimeSpan Duration { get; init; }

	/// <summary>Supplementary facts the check recorded, beyond the pass/fail verdict itself.</summary>
	public IReadOnlyList<ConformanceObservation> Observations { get; init; } = [];

	/// <summary>The subject satisfied the check.</summary>
	public static ConformanceCheckResult Pass(IReadOnlyList<ConformanceObservation>? observations = null)
		=> new() { Outcome = ConformanceOutcome.Passed, Observations = observations ?? [] };

	/// <summary>
	/// The subject violated the check. <paramref name="expected" /> and <paramref name="actual" /> must both
	/// be non-empty - a failure with nothing to compare against is not an actionable report, and
	/// <see cref="ConformanceReportWriter" /> depends on both being present to render one.
	/// </summary>
	public static ConformanceCheckResult Fail(
		string expected,
		string actual,
		IReadOnlyList<ConformanceObservation>? observations = null)
	{
		ArgumentException.ThrowIfNullOrEmpty(expected);
		ArgumentException.ThrowIfNullOrEmpty(actual);

		return new ConformanceCheckResult
		{
			Outcome = ConformanceOutcome.Failed,
			Expected = expected,
			Actual = actual,
			Observations = observations ?? []
		};
	}

	/// <summary>
	/// The check did not run against this subject. <paramref name="reason" /> must be non-empty - a skip is
	/// first-class and is always explained, never silent.
	/// </summary>
	public static ConformanceCheckResult Skip(string reason)
	{
		ArgumentException.ThrowIfNullOrEmpty(reason);
		return new ConformanceCheckResult { Outcome = ConformanceOutcome.Skipped, SkipReason = reason };
	}

	/// <summary>
	/// The check could not reach a verdict for a reason outside the subject's control.
	/// <paramref name="reason" /> must be non-empty.
	/// </summary>
	public static ConformanceCheckResult Inconclusive(string reason)
	{
		ArgumentException.ThrowIfNullOrEmpty(reason);
		return new ConformanceCheckResult { Outcome = ConformanceOutcome.Inconclusive, SkipReason = reason };
	}

	/// <summary>Returns a copy with <see cref="Duration" /> set. Internal: only <see cref="ConformanceRunner" /> times a check.</summary>
	internal ConformanceCheckResult WithDuration(TimeSpan duration) => this with { Duration = duration };
}
