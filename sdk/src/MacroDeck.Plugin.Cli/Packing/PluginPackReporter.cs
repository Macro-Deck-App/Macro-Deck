using MacroDeck.Plugin.Cli.Commands;
using MacroDeck.Plugin.Cli.Manifests;
using MacroDeck.Plugin.Packaging.Artifacts;

namespace MacroDeck.Plugin.Cli.Packing;

/// <summary>Turns a <see cref="PluginPackResult" /> into console output and an exit code - split out of
/// <c>Commands/PackCommand.cs</c> so the mapping is directly callable from tests.</summary>
internal static class PluginPackReporter
{
	public static async Task<int> ReportAsync(CliConsole console,
		PluginPackResult result,
		bool showDigest,
		CancellationToken cancellationToken = default)
	{
		if (!result.Success)
		{
			return ReportFailure(console, result);
		}

		foreach (var warning in result.Warnings)
		{
			console.WriteWarning(warning.Code, warning.Message);
		}

		if (result.CreatedOutputDirectory is { } createdOutputDirectory)
		{
			console.Info($"Created output directory '{CliText.DisplayPath(createdOutputDirectory)}'.");
		}

		console.Info($"Packed {result.PluginId} {result.Version} -> {result.OutputPath} " +
			$"({result.EntryCount} entries, {result.TotalUncompressedBytes} bytes uncompressed).");

		if (showDigest)
		{
			// Recomputed by re-inspecting the artifact just written, rather than kept from PackAsync's own
			// internals, so --show-digest here proves the exact same thing inspect --show-digest against
			// the artifact would - one code path, not two that could disagree.
			var inspection = await ArtifactReaders.ArtifactReader.Inspect(result.OutputPath!, cancellationToken)
				.ConfigureAwait(false);

			if (inspection.Success && inspection.Manifest is { } manifest)
			{
				console.WriteLine("Digest to sign (base64): " +
					Convert.ToBase64String(PluginArtifactDigest.Compute(manifest)));
			}
		}

		return ExitCode.Success;
	}

	private static int ReportFailure(CliConsole console, PluginPackResult result)
	{
		if (result.FailureReason == PluginPackFailureReason.ManifestInvalid && result.Validation is { } validation)
		{
			ValidationResultWriter.Write(console, CliOutputFormat.Text, validation);
			return validation.ExitCode;
		}

		console.WriteError(PluginPackFailureCode.For(result.FailureReason), result.FailureMessage ?? "Packing failed.");

		return PluginPackFailureExitCode.For(result.FailureReason);
	}
}
