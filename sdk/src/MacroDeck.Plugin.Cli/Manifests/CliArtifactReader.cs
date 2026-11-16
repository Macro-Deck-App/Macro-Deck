using MacroDeck.Plugin.Cli.Building;
using MacroDeck.Plugin.Packaging.Artifacts;

namespace MacroDeck.Plugin.Cli.Manifests;

internal sealed class CliArtifactReader(IPluginArtifactReader inner) : IPluginArtifactReader
{
	public async Task<PluginArtifactInspectionResult> Inspect(
		string artifactPath,
		CancellationToken cancellationToken = default)
	{
		try
		{
			return await inner.Inspect(artifactPath, cancellationToken).ConfigureAwait(false);
		}
		catch (InvalidDataException ex)
		{
			return PluginArtifactInspectionResult.Fail(PluginInstallError.InvalidArchive,
				ArtifactErrorText.CorruptEntry(ex.Message));
		}
	}

	public async Task<PluginArtifactExtractResult> ExtractTo(
		string artifactPath,
		string targetDirectory,
		CancellationToken cancellationToken = default)
	{
		try
		{
			return await inner.ExtractTo(artifactPath, targetDirectory, cancellationToken).ConfigureAwait(false);
		}
		catch (InvalidDataException ex)
		{
			BuildStaging.Delete(targetDirectory);

			return PluginArtifactExtractResult.Fail(PluginInstallError.InvalidArchive,
				ArtifactErrorText.CorruptEntry(ex.Message));
		}
	}
}
