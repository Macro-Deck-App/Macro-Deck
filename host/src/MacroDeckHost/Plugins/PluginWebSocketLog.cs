using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Plugins;

internal static class PluginWebSocketLog
{
	public static void ProtocolErrorDeliveryFailed(ILogger logger, Exception exception)
		=> logger.Debug(exception, "Could not deliver a protocol.error to a plugin connection.");

	public static void ConnectionFaulted(ILogger logger, Exception exception)
		=> logger.Warning(exception, "A plugin WebSocket connection ended unexpectedly.");

	public static void PluginProtocolError(ILogger logger, string pluginId, string? code)
		=> logger.Debug("Received a protocol.error from plugin '{PluginId}': {Code}.", pluginId, code);

	public static void StateUpdateRefreshFailed(ILogger logger, string pluginId, string kind, Exception exception)
		=> logger.Debug(exception,
			"Could not refresh the '{Kind}' capability snapshot for plugin '{PluginId}' after a state.update.",
			kind,
			pluginId);

	public static void CapabilityDeclareRejected(ILogger logger, string pluginId, string reason)
		=> logger.Warning("Rejecting a mid-session capability.declare from plugin '{PluginId}': {Reason}.",
			pluginId,
			reason);

	public static void CapabilityDeclareReregisterFailed(ILogger logger, string pluginId, Exception exception)
		=> logger.Debug(exception,
			"Could not re-register plugin '{PluginId}' after a mid-session capability.declare.",
			pluginId);

	public static void AssetStepRejected(ILogger logger, string pluginId, string? assetId, string? errorCode)
		=> logger.Debug("Rejected an asset.* step from plugin '{PluginId}' for asset '{AssetId}': {ErrorCode}.",
			pluginId,
			assetId,
			errorCode);

	public static void PluginRegistrationFailed(ILogger logger, string pluginId, Exception exception)
		=> logger.Error(exception,
			"Registering plugin '{PluginId}' as an integration failed; its session stays open but it " +
			"will not appear in the integration list.",
			pluginId);

	public static void LogPublishRateLimited(ILogger logger, string pluginId, int droppedCount)
		=> logger.Warning(
			"Plugin '{PluginId}' is exceeding its log.publish rate limit; {DroppedCount} event(s) dropped.",
			pluginId,
			droppedCount);

	public static void LogPublishFailed(ILogger logger, string pluginId, Exception exception)
		=> logger.Warning(exception,
			"Plugin '{PluginId}' sent a log.publish batch that could not be ingested.",
			pluginId);

	public static void RegistrationSessionRefusedForLiveLaunch(ILogger logger, string pluginId)
		=> logger.Warning(
			"Refused a registration-authenticated session for plugin '{PluginId}': the supervisor has a live " +
			"managed launch for that id.",
			pluginId);
}
