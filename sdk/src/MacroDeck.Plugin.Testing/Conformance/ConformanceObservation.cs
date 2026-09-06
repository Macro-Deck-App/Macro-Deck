namespace MacroDeck.Plugin.Testing.Conformance;

/// <summary>
/// One supplementary, human-readable fact a check recorded while it ran - a count, a sampled value, which
/// declared capability it exercised. Never load-bearing for the verdict itself: everything that decides
/// <see cref="ConformanceCheckResult.Outcome" /> belongs in <see cref="ConformanceCheckResult.Expected" />
/// and <see cref="ConformanceCheckResult.Actual" /> instead, so a reader never has to parse an observation
/// to understand why a check passed or failed.
/// </summary>
public sealed record ConformanceObservation
{
	/// <summary>What this observation is about, e.g. <c>"declared capabilities"</c>.</summary>
	public required string Label { get; init; }

	/// <summary>The observed value, rendered as text.</summary>
	public required string Detail { get; init; }
}
