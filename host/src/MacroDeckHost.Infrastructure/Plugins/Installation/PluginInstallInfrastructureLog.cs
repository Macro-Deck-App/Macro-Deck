using Serilog;

namespace MacroDeckHost.Infrastructure.Plugins.Installation;

internal static class PluginInstallInfrastructureLog
{
	public static void CacheIndexWriteFailed(ILogger logger, string indexPath, Exception exception)
		=> logger.Error(exception, "Failed to write the plugin artifact cache index at '{IndexPath}'.", indexPath);

	public static void CacheEntryFileMissing(ILogger logger, string sha256, string cachedPath)
		=> logger.Debug("Dropping plugin artifact cache entry '{Sha256}'; its file at '{CachedPath}' no longer exists.",
			sha256,
			cachedPath);

	public static void CacheEntryEvicted(ILogger logger, string sha256, long budgetBytes)
		=> logger.Debug(
			"Evicting least-recently-used plugin artifact cache entry '{Sha256}' to stay within the {BudgetBytes}-byte budget.",
			sha256,
			budgetBytes);

	public static void CacheOrphanFileDeleted(ILogger logger, string filePath)
		=> logger.Warning(
			"Deleting orphaned plugin artifact cache file '{FilePath}', which the index does not reference.",
			filePath);

	public static void CacheFileDeleteFailed(ILogger logger, string filePath, Exception exception)
		=> logger.Warning(exception, "Failed to delete plugin artifact cache file '{FilePath}'.", filePath);

	public static void ArtifactDownloadFailed(ILogger logger, string url, Exception exception)
		=> logger.Warning(exception, "Failed to download plugin artifact from '{Url}'.", url);

	public static void ArtifactStagingFailed(ILogger logger, string stagingPath, Exception exception)
		=> logger.Warning(exception, "Failed to stage plugin artifact into '{StagingPath}'.", stagingPath);
}
