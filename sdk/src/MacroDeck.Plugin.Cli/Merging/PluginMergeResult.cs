using MacroDeck.Plugin.Cli.Packing;
using MacroDeck.Plugin.Packaging.Artifacts;

namespace MacroDeck.Plugin.Cli.Merging;

internal enum PluginMergeFailureReason
{
	ArtifactRejected,

	IdentityMismatch,

	ManifestMismatch,

	DuplicateRid,

	FileConflict,

	OutputExists,

	StagingFailed
}

internal sealed record PluginMergeResult
{
	public required bool Success { get; init; }

	public IReadOnlyList<string> MergedRids { get; init; } = [];

	public PluginPackResult? Pack { get; init; }

	public PluginMergeFailureReason? FailureReason { get; init; }

	public string? FailureMessage { get; init; }

	public string? ArtifactPath { get; init; }

	public PluginInstallError? ArtifactError { get; init; }

	public static PluginMergeResult Packed(IReadOnlyList<string> mergedRids, PluginPackResult pack)
		=> new() { Success = pack.Success, MergedRids = mergedRids, Pack = pack };

	public static PluginMergeResult Fail(PluginMergeFailureReason reason, string message)
		=> new() { Success = false, FailureReason = reason, FailureMessage = message };

	public static PluginMergeResult Rejected(string artifactPath, PluginInstallError error, string? message)
		=> new()
		{
			Success = false,
			FailureReason = PluginMergeFailureReason.ArtifactRejected,
			FailureMessage = message,
			ArtifactPath = artifactPath,
			ArtifactError = error
		};
}
