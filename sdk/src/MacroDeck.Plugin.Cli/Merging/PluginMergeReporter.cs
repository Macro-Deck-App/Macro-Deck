using MacroDeck.Plugin.Cli.Manifests;
using MacroDeck.Plugin.Cli.Packing;
using MacroDeck.Plugin.Packaging.Artifacts;

namespace MacroDeck.Plugin.Cli.Merging;

internal static class PluginMergeReporter
{
	public static async Task<int> ReportAsync(CliConsole console,
		PluginMergeResult result,
		CancellationToken cancellationToken = default)
	{
		if (result.FailureReason is PluginMergeFailureReason.ArtifactRejected)
		{
			var error = result.ArtifactError ?? PluginInstallError.Failed;
			var diagnostic = ArtifactErrorText.Describe(result.ArtifactPath ?? string.Empty,
				error,
				$"'{CliText.DisplayPath(result.ArtifactPath ?? string.Empty)}': {result.FailureMessage}");
			console.WriteError(diagnostic.Code, diagnostic.Message);

			return PluginInstallErrorExitCode.For(error);
		}

		if (result.FailureReason is { } reason)
		{
			console.WriteError(Code(reason), result.FailureMessage ?? "Merging failed.");

			return ExitCodeFor(reason);
		}

		if (result.Pack is not { } pack)
		{
			console.WriteError("merge-failed", "Merging produced no result.");

			return ExitCode.InternalError;
		}

		if (pack.Success)
		{
			console.Info($"Merged {string.Join(", ", result.MergedRids)}.");
		}

		return await PluginPackReporter.ReportAsync(console, pack, showDigest: false, cancellationToken)
			.ConfigureAwait(false);
	}

	public static string Code(PluginMergeFailureReason reason) => reason switch
	{
		PluginMergeFailureReason.ArtifactRejected => "artifact-rejected",
		PluginMergeFailureReason.IdentityMismatch => "identity-mismatch",
		PluginMergeFailureReason.ManifestMismatch => "manifest-mismatch",
		PluginMergeFailureReason.DuplicateRid => "duplicate-rid",
		PluginMergeFailureReason.FileConflict => "file-conflict",
		PluginMergeFailureReason.OutputExists => "output-exists",
		PluginMergeFailureReason.StagingFailed => "staging-failed",
		_ => "merge-failed"
	};

	public static int ExitCodeFor(PluginMergeFailureReason reason) => reason switch
	{
		PluginMergeFailureReason.ArtifactRejected => ExitCode.SubjectInvalid,
		PluginMergeFailureReason.IdentityMismatch => ExitCode.SubjectInvalid,
		PluginMergeFailureReason.ManifestMismatch => ExitCode.SubjectInvalid,
		PluginMergeFailureReason.DuplicateRid => ExitCode.SubjectInvalid,
		PluginMergeFailureReason.FileConflict => ExitCode.SubjectInvalid,
		PluginMergeFailureReason.OutputExists => ExitCode.UsageError,
		PluginMergeFailureReason.StagingFailed => ExitCode.InternalError,
		_ => ExitCode.InternalError
	};
}
