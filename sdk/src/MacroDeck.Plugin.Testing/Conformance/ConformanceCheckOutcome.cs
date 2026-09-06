namespace MacroDeck.Plugin.Testing.Conformance;

/// <summary>
/// One check's metadata paired with what it concluded. <see cref="ConformanceReport.Results" /> carries
/// exactly one of these per member of <see cref="ConformanceRunner.Checks" /> the run selected - see
/// <see cref="ConformanceOptions" /> - never fewer, so a report can always be read without cross-referencing
/// the check list it was produced from.
/// </summary>
public sealed record ConformanceCheckOutcome
{
	/// <summary>The check's id. See <see cref="IConformanceCheck.Id" />.</summary>
	public required string Id { get; init; }

	/// <summary>The check's title. See <see cref="IConformanceCheck.Title" />.</summary>
	public required string Title { get; init; }

	/// <summary>The check's category.</summary>
	public required ConformanceCategory Category { get; init; }

	/// <summary>The check's requirement level.</summary>
	public required ConformanceRequirement Requirement { get; init; }

	/// <summary>What the check concluded.</summary>
	public required ConformanceCheckResult Result { get; init; }
}
