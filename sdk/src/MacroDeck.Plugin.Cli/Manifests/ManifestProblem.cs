using MacroDeck.Plugin.Packaging.Manifest;

namespace MacroDeck.Plugin.Cli.Manifests;

/// <summary>How seriously a <see cref="ManifestProblem" /> should be taken. A <see cref="Warning" /> alone
/// never fails validation - only an <see cref="Error" /> does.</summary>
internal enum ManifestProblemSeverity
{
	Warning,
	Error
}

/// <summary>
/// One thing <see cref="ManifestValidator" /> found wrong (or worth mentioning) about a manifest, from
/// whichever layer noticed it - the real <c>IPluginManifestReader</c>, embedded JSON Schema evaluation,
/// the permission vocabulary, or a declared file digest checked against disk.
/// </summary>
internal sealed record ManifestProblem
{
	public required ManifestProblemSeverity Severity { get; init; }

	/// <summary>A stable, kebab-case identifier for what kind of problem this is, e.g.
	/// <c>unknown-permission</c> or <c>schema:required</c>.</summary>
	public required string Code { get; init; }

	public required string Message { get; init; }

	/// <summary>An RFC 6901 JSON pointer into the manifest document, when the problem can be located that
	/// precisely. Null for a problem <see cref="Packaging.Manifest.IPluginManifestReader" /> reported,
	/// which has no pointer of its own to offer.</summary>
	public string? Pointer { get; init; }

	/// <summary>The <see cref="PluginManifestValidationLevel" /> that requires this problem to be fixed -
	/// null for a level-independent defect (malformed JSON, a schema violation, a reader error) that is
	/// wrong at every level rather than only from some level upward.</summary>
	public PluginManifestValidationLevel? Level { get; init; }
}
