using MacroDeck.Plugin.Packaging.Artifacts;
using MacroDeck.Plugin.Packaging.Manifest;
using Serilog.Core;

namespace MacroDeck.Plugin.Cli.Manifests;

/// <summary>The one <see cref="IPluginManifestReader" />/<see cref="IPluginArtifactReader" /> pair every
/// command shares, so <c>validate</c>, <c>inspect</c> and <c>pack</c> can never end up reading a manifest
/// or artifact through subtly different reader instances.</summary>
internal static class ArtifactReaders
{
	public static readonly IPluginManifestReader ManifestReader = new PluginManifestReader();

	public static readonly IPluginArtifactReader ArtifactReader =
		new PluginArtifactReader(ManifestReader, Logger.None);
}
