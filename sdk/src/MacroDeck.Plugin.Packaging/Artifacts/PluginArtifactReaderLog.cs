using Serilog;

namespace MacroDeck.Plugin.Packaging.Artifacts;

/// <summary>Log messages for <see cref="PluginArtifactReader"/>. Split out of the host's own
/// <c>PluginInstallInfrastructureLog</c> when the reader moved here.</summary>
internal static class PluginArtifactReaderLog
{
	public static void ExtractCleanupFailed(ILogger logger, string targetDirectory, Exception exception)
		=> logger.Warning(exception,
			"Failed to clean up partially extracted plugin artifact at '{TargetDirectory}'.",
			targetDirectory);
}
