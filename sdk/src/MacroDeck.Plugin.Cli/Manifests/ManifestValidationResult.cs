using MacroDeck.Plugin.Packaging.Manifest;

namespace MacroDeck.Plugin.Cli.Manifests;

/// <summary>What <see cref="ManifestValidator" /> concluded about one manifest.</summary>
internal sealed record ManifestValidationResult
{
	/// <summary>True when <see cref="Problems" /> contains no <see cref="ManifestProblemSeverity.Error" />
	/// entry. A manifest carrying only warnings is still valid.</summary>
	public required bool Valid { get; init; }

	/// <summary>The exit code this result implies: <see cref="ExitCode.Success" />,
	/// <see cref="ExitCode.SubjectInvalid" /> or <see cref="ExitCode.InputUnreadable" />.</summary>
	public required int ExitCode { get; init; }

	/// <summary>The manifest's declared id, when it could be read at all.</summary>
	public string? PluginId { get; init; }

	/// <summary>The manifest's declared version, when it could be read at all.</summary>
	public string? Version { get; init; }

	/// <summary>Every problem found, most-significant first. Empty means a clean manifest.</summary>
	public required IReadOnlyList<ManifestProblem> Problems { get; init; }

	/// <summary>The parsed manifest, when reading got far enough to produce one - null once the real
	/// reader itself failed.</summary>
	public PluginManifest? Manifest { get; init; }

	/// <summary>The resolved absolute path of whatever was validated - a manifest file or an artifact.
	/// Set on every result, so a caller can identify the subject even when <see cref="Manifest" /> is null
	/// and <see cref="PluginId" />/<see cref="Version" /> cannot be trusted as a heading.</summary>
	public string? Subject { get; init; }

	/// <summary>The <see cref="PluginManifestValidationLevel" /> this run was evaluated at. Defaults to
	/// <see cref="PluginManifestValidationLevel.Development" /> rather than being required, so every
	/// existing construction site keeps compiling.</summary>
	public PluginManifestValidationLevel Level { get; init; } = PluginManifestValidationLevel.Development;
}
