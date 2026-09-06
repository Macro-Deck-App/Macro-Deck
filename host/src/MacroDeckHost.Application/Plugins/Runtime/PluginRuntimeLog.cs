using Serilog;

namespace MacroDeckHost.Application.Plugins.Runtime;

internal static class PluginRuntimeLog
{
	public static void SendToPluginNoConnection(ILogger logger, string pluginId)
		=> logger.Debug("Could not deliver an envelope to plugin '{PluginId}': no attached session connection.",
			pluginId);

	public static void IdentityReconciliationRevoked(ILogger logger, string pluginId)
		=> logger.Warning(
			"Startup identity reconciliation revoked plugin '{PluginId}''s developer enrollment: the id is " +
			"also installed, and the two must not both claim it.",
			pluginId);
}
