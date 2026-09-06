using MacroDeck.Plugin.Packaging.Manifest;

namespace MacroDeck.Plugin.Packaging.Artifacts;

/// <summary>What an artifact turned out to contain, without extracting it.</summary>
public sealed record PluginArtifactInspectionResult
{
	public required bool Success { get; init; }

	public PluginManifest? Manifest { get; init; }

	public int EntryCount { get; init; }

	public long TotalUncompressedBytes { get; init; }

	public PluginInstallError? Error { get; init; }

	public string? ErrorMessage { get; init; }

	public static PluginArtifactInspectionResult Ok(PluginManifest manifest,
		int entryCount,
		long totalUncompressedBytes)
	{
		return new PluginArtifactInspectionResult
		{
			Success = true,
			Manifest = manifest,
			EntryCount = entryCount,
			TotalUncompressedBytes = totalUncompressedBytes
		};
	}

	public static PluginArtifactInspectionResult Fail(PluginInstallError error, string message)
	{
		return new PluginArtifactInspectionResult { Success = false, Error = error, ErrorMessage = message };
	}
}

/// <summary>What extraction produced.</summary>
public sealed record PluginArtifactExtractResult
{
	public required bool Success { get; init; }

	/// <summary>Paths written, relative to the target directory, forward-slash separated. Used to check
	/// declared digests and to reject undeclared files.</summary>
	public IReadOnlyList<string> ExtractedFiles { get; init; } = [];

	public long TotalUncompressedBytes { get; init; }

	public PluginInstallError? Error { get; init; }

	public string? ErrorMessage { get; init; }

	public static PluginArtifactExtractResult Ok(IReadOnlyList<string> extractedFiles,
		long totalUncompressedBytes)
	{
		return new PluginArtifactExtractResult
		{
			Success = true,
			ExtractedFiles = extractedFiles,
			TotalUncompressedBytes = totalUncompressedBytes
		};
	}

	public static PluginArtifactExtractResult Fail(PluginInstallError error, string message)
	{
		return new PluginArtifactExtractResult { Success = false, Error = error, ErrorMessage = message };
	}
}

/// <summary>
/// Reads a <c>.macroDeckPlugin</c> archive. The archive root is the version directory, so extraction is a
/// verbatim copy of the artifact's contents and nothing is rewritten on the way in.
/// </summary>
public interface IPluginArtifactReader
{
	/// <summary>Opens the archive, enforces the structural limits and parses the manifest. Writes nothing.
	/// </summary>
	Task<PluginArtifactInspectionResult> Inspect(string artifactPath,
		CancellationToken cancellationToken = default);

	/// <summary>Extracts every entry into <paramref name="targetDirectory"/>, applying
	/// <see cref="PluginArtifactEntryPolicy"/> and the byte caps to each one.</summary>
	Task<PluginArtifactExtractResult> ExtractTo(string artifactPath,
		string targetDirectory,
		CancellationToken cancellationToken = default);
}
