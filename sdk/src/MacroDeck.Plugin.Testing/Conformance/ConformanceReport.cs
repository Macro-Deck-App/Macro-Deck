namespace MacroDeck.Plugin.Testing.Conformance;

/// <summary>The result of running a <see cref="ConformanceRunner" />'s checks against one <see cref="ConformanceSubject" />.</summary>
public sealed record ConformanceReport
{
	/// <summary>The version of the check suite that produced this report. See <see cref="ConformanceRunner.SuiteVersion" />.</summary>
	public required string SuiteVersion { get; init; }

	/// <summary>The subject's declared plugin id, read from wherever the subject reported it. Null when the subject never connected far enough to report one.</summary>
	public string? PluginId { get; init; }

	/// <summary>The subject's declared plugin version, read from wherever the subject reported it. Null when the subject never connected far enough to report one.</summary>
	public string? PluginVersion { get; init; }

	/// <summary>When the run started.</summary>
	public required DateTimeOffset StartedAt { get; init; }

	/// <summary>How long the whole run took, including establishing the subject's ambient session.</summary>
	public required TimeSpan Duration { get; init; }

	/// <summary>One entry per selected check, in <see cref="ConformanceRunner.Checks" /> order. Never has a gap: every selected check contributes exactly one result, whatever it concluded.</summary>
	public required IReadOnlyList<ConformanceCheckOutcome> Results { get; init; }

	/// <summary>How many entries in <see cref="Results" /> have outcome <see cref="ConformanceOutcome.Passed" />.</summary>
	public required int Passed { get; init; }

	/// <summary>How many entries in <see cref="Results" /> have outcome <see cref="ConformanceOutcome.Failed" />.</summary>
	public required int Failed { get; init; }

	/// <summary>
	/// How many entries in <see cref="Results" /> have outcome <see cref="ConformanceOutcome.Skipped" /> or
	/// <see cref="ConformanceOutcome.Inconclusive" /> - the two share this one bucket because neither ever
	/// affects <see cref="Conformant" />. <see cref="Passed" /> + <see cref="Failed" /> + this always equals
	/// <see cref="Results" />'s count.
	/// </summary>
	public required int Skipped { get; init; }

	/// <summary>
	/// True when no <see cref="ConformanceRequirement.Required" /> entry in <see cref="Results" /> has
	/// outcome <see cref="ConformanceOutcome.Failed" />. A <see cref="ConformanceRequirement.Recommended" />
	/// failure, and any <see cref="ConformanceOutcome.Skipped" /> or <see cref="ConformanceOutcome.Inconclusive" />
	/// result regardless of requirement level, never affects this.
	/// </summary>
	public required bool Conformant { get; init; }
}
