using Serilog;

namespace MacroDeckHost.Infrastructure.Plugins.Installation;

internal static class PluginInstallerLog
{
	public static void Installing(ILogger logger, string pluginId, string version)
		=> logger.Information("Installing plugin '{PluginId}' version {Version}.", pluginId, version);

	public static void Activated(ILogger logger, string pluginId, string version, string previousVersion)
		=> logger.Information("Activated plugin '{PluginId}' version {Version}, replacing {PreviousVersion}.",
			pluginId,
			version,
			previousVersion);

	public static void HealthValidationFailed(ILogger logger, string pluginId, string version)
		=> logger.Warning("Plugin '{PluginId}' version {Version} did not become healthy; rolling back.",
			pluginId,
			version);

	public static void RolledBack(ILogger logger, string pluginId, string previousVersion)
		=> logger.Warning("Rolled plugin '{PluginId}' back to {PreviousVersion}.", pluginId, previousVersion);

	public static void RollbackFailed(ILogger logger, string pluginId, Exception exception)
		=> logger.Error(exception,
			"Rolling plugin '{PluginId}' back failed; it may be left without an active version.",
			pluginId);

	public static void Uninstalled(ILogger logger, string pluginId, bool keepData)
		=> logger.Information("Uninstalled plugin '{PluginId}' (data retained: {KeepData}).", pluginId, keepData);

	public static void DeleteFailed(ILogger logger, string path, int attempts, Exception exception)
		=> logger.Warning(exception,
			"Could not delete '{Path}' after {Attempts} attempts; a file there is still in use.",
			path,
			attempts);

	public static void PrunedVersion(ILogger logger, string pluginId, string version)
		=> logger.Debug("Pruned plugin '{PluginId}' version {Version}.", pluginId, version);

	public static void CompatibilityCheckSkipped(ILogger logger, string pluginId)
		=> logger.Warning(
			"Skipping the Macro Deck compatibility check for '{PluginId}': the host reports a development version.",
			pluginId);

	public static void StagingCleanupFailed(ILogger logger, string path, Exception exception)
		=> logger.Warning(exception, "Could not clean the staging directory '{Path}'.", path);

	public static void IdClaimFailed(ILogger logger, string pluginId, Exception exception)
		=> logger.Warning(exception,
			"Could not revoke a developer enrollment for plugin id '{PluginId}' after install; the install " +
			"itself succeeded.",
			pluginId);
}
