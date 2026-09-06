using MacroDeckHost.Application.Plugins.Trust;
using Serilog;

namespace MacroDeckHost.Infrastructure.Plugins;

internal static class PluginInfrastructureLog
{
	public static void IntegrityCheckFailed(ILogger logger, string pluginId, PluginTrustVerdict verdict)
		=> logger.Warning(
			"Plugin '{PluginId}' failed its launch-time integrity check ({Verdict}); refusing to spawn it.",
			pluginId,
			verdict);

	public static void LaunchFailed(ILogger logger, string executablePath, Exception exception)
		=> logger.Error(exception, "Failed to start plugin process '{ExecutablePath}'.", executablePath);

	public static void KillingProcessTree(ILogger logger, int processId)
		=> logger.Warning("Killing plugin process tree, root PID {ProcessId}.", processId);

	public static void KillTreeFallback(ILogger logger, int processId, Exception exception)
		=> logger.Warning(exception,
			"Killing plugin process tree for PID {ProcessId} failed; falling back to killing only the root process.",
			processId);

	public static void KillFailed(ILogger logger, int processId, Exception exception)
		=> logger.Error(exception,
			"Killing plugin process PID {ProcessId} failed entirely; it may be orphaned.",
			processId);

	public static void HealthProbeFailed(ILogger logger, string pluginId, string address, Exception exception)
		=> logger.Debug(exception, "Health probe for plugin '{PluginId}' at '{Address}' failed.", pluginId, address);

	public static void StartFailed(ILogger logger, string pluginId, Exception exception)
		=> logger.Error(exception, "Starting plugin '{PluginId}' failed unexpectedly.", pluginId);

	public static void StopFailed(ILogger logger, string pluginId, Exception exception)
		=> logger.Error(exception, "Stopping plugin '{PluginId}' failed unexpectedly.", pluginId);

	public static void RestartFailed(ILogger logger, string pluginId, Exception exception)
		=> logger.Error(exception, "Restarting plugin '{PluginId}' failed unexpectedly.", pluginId);

	public static void StopAllEntryFailed(ILogger logger, string pluginId, Exception exception)
		=> logger.Error(exception, "Stopping plugin '{PluginId}' during StopAll failed unexpectedly.", pluginId);

	public static void ReconcileEntryFailed(ILogger logger, string pluginId, Exception exception)
		=> logger.Error(exception, "Reconciling plugin '{PluginId}' failed unexpectedly.", pluginId);

	public static void ReconcileTickFailed(ILogger logger, Exception exception)
		=> logger.Error(exception, "Plugin supervisor reconcile tick failed unexpectedly.");

	public static void MetadataSeedReadFailed(ILogger logger, string pluginId, Exception exception)
		=> logger.Warning(exception,
			"Reading the manifest to seed metadata for never-started plugin '{PluginId}' failed; falling " +
			"back to the plugin id as its name.",
			pluginId);

	public static void HealthProbeThrew(ILogger logger, string pluginId, Exception exception)
		=> logger.Debug(exception, "Health probe for plugin '{PluginId}' threw unexpectedly.", pluginId);

	public static void GoodbyeSendFailed(ILogger logger, string pluginId, Exception exception)
		=> logger.Debug(exception, "Sending session.goodbye to plugin '{PluginId}' failed.", pluginId);

	public static void SessionCloseFailed(ILogger logger, string pluginId, Exception exception)
		=> logger.Debug(exception, "Closing the session for plugin '{PluginId}' failed.", pluginId);

	public static void ObserveExitFailed(ILogger logger, string pluginId, Exception exception)
		=> logger.Error(exception, "Observing exit of plugin '{PluginId}' failed unexpectedly.", pluginId);

	public static void DiscoverFailed(ILogger logger, Exception exception)
		=> logger.Error(exception, "Discovering installed plugins failed unexpectedly.");

	public static void PublishRuntimeChangedFailed(ILogger logger, Exception exception)
		=> logger.Error(exception, "Publishing plugin runtime change failed unexpectedly.");

	public static void BackgroundTerminationFailed(ILogger logger, string pluginId, Exception exception)
		=> logger.Error(exception, "Background termination of plugin '{PluginId}' failed unexpectedly.", pluginId);

	public static void ProcessSurvivedKill(ILogger logger, string pluginId, int processId)
		=> logger.Error(
			"Plugin '{PluginId}' (PID {ProcessId}) is still running after a kill; it is orphaned until the OS reaps it.",
			pluginId,
			processId);

	public static void DotnetMuxerProbeTimedOut(ILogger logger, string executablePath)
		=> logger.Warning(
			"Probing '{ExecutablePath}' with --list-runtimes timed out; treating it as having no installed runtimes.",
			executablePath);

	public static void DotnetMuxerProbeFailed(ILogger logger, string executablePath, Exception exception)
		=> logger.Warning(exception,
			"Probing '{ExecutablePath}' with --list-runtimes failed; treating it as having no installed runtimes.",
			executablePath);

	public static void ProcessLookupFailed(ILogger logger, int processId, Exception exception)
		=> logger.Debug(exception, "Looking up process PID {ProcessId} in the process table failed.", processId);

	public static void OrphanSweepSkipped(ILogger logger, int ownerProcessId)
		=> logger.Information(
			"The plugin process journal is owned by live host PID {OwnerProcessId}; leaving its plugins alone.",
			ownerProcessId);

	public static void OrphanPidReused(ILogger logger, string pluginId, int processId)
		=> logger.Warning(
			"PID {ProcessId} journalled for plugin '{PluginId}' now belongs to a different process; dropping the " +
			"entry without killing anything.",
			processId,
			pluginId);

	public static void OrphanKilled(ILogger logger, string pluginId, int processId)
		=> logger.Warning("Killed orphaned plugin '{PluginId}' (PID {ProcessId}) left by a previous host session.",
			pluginId,
			processId);

	public static void OrphanKillFailed(ILogger logger, string pluginId, int processId, Exception exception)
		=> logger.Error(exception,
			"Killing orphaned plugin '{PluginId}' (PID {ProcessId}) failed.",
			pluginId,
			processId);

	public static void JobAssignFailed(ILogger logger, int processId)
		=> logger.Warning(
			"Assigning plugin PID {ProcessId} to its job object failed; it will not be terminated automatically " +
			"when the host dies.",
			processId);

	public static void JobOpenProcessFailed(ILogger logger, int processId, int lastError)
		=> logger.Warning("Opening plugin PID {ProcessId} to assign it to a job object failed with {LastError}.",
			processId,
			lastError);
}
