using Serilog;

namespace MacroDeckHost.Application.Plugins.Capabilities;

internal static class PluginCapabilityLog
{
	public static void LateResultDropped(ILogger logger, string pluginId, string correlationId)
		=> logger.Debug(
			"Dropped a late capability.result from plugin '{PluginId}' for abandoned correlation '{CorrelationId}'.",
			pluginId,
			correlationId);

	public static void CancelDeliveryFailed(ILogger logger, string pluginId, string correlationId, Exception exception)
		=> logger.Debug(exception,
			"Could not deliver capability.cancel to plugin '{PluginId}' for correlation '{CorrelationId}'.",
			pluginId,
			correlationId);

	public static void SendAbandoned(ILogger logger, string pluginId, string correlationId, Exception exception)
		=> logger.Debug(exception,
			"The send for plugin '{PluginId}' correlation '{CorrelationId}' was abandoned after the invoke budget expired and then failed.",
			pluginId,
			correlationId);
}
