using MacroDeck.Plugin.Cli.Manifests;

namespace MacroDeck.Plugin.Cli.Packing;

internal enum PluginPackFailureReason
{
	/// <summary>The manifest failed <see cref="ManifestValidator" /> - reported through
	/// <see cref="PluginPackResult.Validation" />, not a message of its own.</summary>
	ManifestInvalid,

	/// <summary>A source entry <see cref="Packaging.Artifacts.PluginArtifactEntryPolicy" /> or
	/// <see cref="Packaging.Artifacts.PluginArtifactLimits" /> would reject.</summary>
	SourceEntryRejected,

	/// <summary>The recomputed manifest itself is too large to pack.</summary>
	LimitExceeded,

	/// <summary><paramref name="OutputPath" /> already exists and <c>--force</c> was not given.</summary>
	OutputExists,

	SourceNotFound,

	WriteFailed
}

/// <summary>What <see cref="PluginPacker.PackAsync" /> produced.</summary>
internal sealed record PluginPackResult
{
	public required bool Success { get; init; }

	public string? OutputPath { get; init; }

	public string? PluginId { get; init; }

	public string? Version { get; init; }

	public int EntryCount { get; init; }

	public long TotalUncompressedBytes { get; init; }

	public PluginPackFailureReason? FailureReason { get; init; }

	public string? FailureMessage { get; init; }

	/// <summary>Set only when <see cref="FailureReason" /> is <see cref="PluginPackFailureReason.ManifestInvalid" />
	/// - the full validation result, so a caller can report every problem, not just the first.</summary>
	public ManifestValidationResult? Validation { get; init; }

	/// <summary>Non-fatal problems worth surfacing even though packing still succeeded - a missing foreign
	/// entrypoint, a Debug-looking source tree. The exit code is unaffected by these; see
	/// <see cref="PluginPackReporter" />.</summary>
	public IReadOnlyList<CliDiagnostic> Warnings { get; init; } = [];

	/// <summary>Set when the output directory did not exist before packing and had to be created. Reported
	/// as narration, not a warning - see <see cref="PluginPackReporter" />.</summary>
	public string? CreatedOutputDirectory { get; init; }

	public static PluginPackResult Ok(string outputPath,
		string pluginId,
		string version,
		int entryCount,
		long totalUncompressedBytes,
		IReadOnlyList<CliDiagnostic>? warnings = null,
		string? createdOutputDirectory = null)
	{
		return new PluginPackResult
		{
			Success = true,
			OutputPath = outputPath,
			PluginId = pluginId,
			Version = version,
			EntryCount = entryCount,
			TotalUncompressedBytes = totalUncompressedBytes,
			Warnings = warnings ?? [],
			CreatedOutputDirectory = createdOutputDirectory
		};
	}

	public static PluginPackResult ValidationFailed(ManifestValidationResult validation)
	{
		return new PluginPackResult
		{
			Success = false,
			FailureReason = PluginPackFailureReason.ManifestInvalid,
			FailureMessage = "The manifest is not valid.",
			Validation = validation
		};
	}

	public static PluginPackResult Fail(PluginPackFailureReason reason, string message)
	{
		return new PluginPackResult { Success = false, FailureReason = reason, FailureMessage = message };
	}
}
