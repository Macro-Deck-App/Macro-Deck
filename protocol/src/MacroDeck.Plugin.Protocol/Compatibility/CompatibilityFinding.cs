namespace MacroDeck.Plugin.Protocol.Compatibility;

/// <summary>
/// One thing the host found about a plugin's compatibility, carrying everything the UI needs to render
/// an actionable row without a second lookup.
///
/// <para>
/// <see cref="Source" /> is not decoration: it is what stops an inference being read as a fact. A
/// finding with <see cref="CompatibilityFindingSources.Inferred" /> says the plugin <em>may</em> use the
/// named API, and the UI is expected to word it that way.
/// </para>
/// </summary>
public sealed record CompatibilityFinding
{
	/// <summary>
	/// The analyzer id this corresponds to (e.g. <c>MDP5002</c>), so the message a plugin author saw at
	/// build time and the one their user sees in the UI are the same finding.
	/// </summary>
	public required string DiagnosticId { get; init; }

	/// <summary>One of <see cref="CompatibilityFindingSources" />.</summary>
	public required string Source { get; init; }

	/// <summary>One of <see cref="CompatibilitySeverities" />.</summary>
	public required string Severity { get; init; }

	/// <summary>
	/// What the finding is about: a deprecated API's display name, a capability kind, or
	/// <c>protocol</c>. Free text, because a host cannot enumerate the APIs of a newer SDK.
	/// </summary>
	public required string Subject { get; init; }

	/// <summary>What to do about it. Never empty - a finding without guidance is not actionable.</summary>
	public required string Guidance { get; init; }

	public string? DeprecatedIn { get; init; }

	public string? RemovedIn { get; init; }

	public string? Replacement { get; init; }

	public string? MigrationUrl { get; init; }
}
