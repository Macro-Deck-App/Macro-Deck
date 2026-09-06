using Serilog;

namespace MacroDeck.Plugin.Hosting.Logging;

/// <summary>
/// Every log message the SDK writes, as extension methods over Serilog's <see cref="ILogger" /> - the
/// same pipeline <c>MacroDeck.Plugin.Serilog</c> attaches its sink to, so an SDK message reaches the
/// host's log viewer the same way a plugin author's own does. One place per message keeps the
/// templates (and their property names) stable for anything reading them.
/// </summary>
internal static class HostingLog
{
	// The plugin's id comes from the manifest, so it is the same for every integration in the process:
	// the type name is what tells two of them apart, and a plugin that registers only one still reads
	// naturally. An integration no longer states an id of its own.
	public static void IntegrationInitialized(this ILogger logger, string pluginId, string integrationType)
		=> logger.Information("Initialized integration '{IntegrationType}' of plugin '{PluginId}'.",
			integrationType,
			pluginId);

	public static void IntegrationInitializationFailed(
		this ILogger logger,
		string pluginId,
		string integrationType,
		Exception exception)
		=> logger.Error(exception,
			"Integration '{IntegrationType}' of plugin '{PluginId}' failed to initialize and will not be usable.",
			integrationType,
			pluginId);

	public static void IntegrationShutdownFailed(
		this ILogger logger,
		string pluginId,
		string integrationType,
		Exception exception)
		=> logger.Error(exception,
			"Integration '{IntegrationType}' of plugin '{PluginId}' failed to shut down cleanly.",
			integrationType,
			pluginId);

	public static void CredentialsUnreadable(this ILogger logger, string path, Exception exception)
		=> logger.Warning(exception,
			"Stored plugin credentials at {Path} could not be read; registering again.",
			path);

	public static void Registered(this ILogger logger, string pluginId)
		=> logger.Information("Registered with the host as '{PluginId}'.", pluginId);

	public static void Reconnecting(this ILogger logger, string reason, TimeSpan delay)
		=> logger.Warning("Disconnected from the host ({Reason}). Retrying in {Delay}.", reason, delay);

	public static void CapabilityRejected(this ILogger logger, string kind, string? reason)
		=> logger.Warning("The host rejected the '{Kind}' capability: {Reason}", kind, reason);

	public static void ConnectionFaulted(this ILogger logger, string reason)
		=> logger.Error("The plugin cannot connect to the host: {Reason}", reason);

	public static void SessionTeardownFailed(this ILogger logger, Exception exception)
		=> logger.Debug(exception, "The session could not be ended explicitly.");

	public static void GracefulCloseFailed(this ILogger logger, Exception exception)
		=> logger.Debug(exception, "The session could not be closed gracefully.");

	public static void MessageTooLarge(this ILogger logger, string reason)
		=> logger.Warning("The host sent an oversize message: {Reason}", reason);

	public static void KeepAliveTimedOut(this ILogger logger, TimeSpan silence)
		=> logger.Warning("No traffic from the host for {Silence}; dropping the connection.", silence);

	public static void HostProtocolError(this ILogger logger, string? code)
		=> logger.Warning("The host reported a protocol error: {Code}.", code);

	public static void HostEndedSession(this ILogger logger)
		=> logger.Information("The host ended the session.");

	public static void UnhandledMessageType(this ILogger logger, string type)
		=> logger.Debug("Nothing handles '{Type}' yet; ignoring it.", type);

	public static void CapabilityDispatchFailed(this ILogger logger, string correlationId, Exception exception)
		=> logger.Error(exception,
			"Dispatching the capability invocation '{CorrelationId}' failed outside the handler itself.",
			correlationId);

	public static void CapabilityThrew(
		this ILogger logger,
		string kind,
		string localId,
		string operation,
		Exception exception)
		=> logger.Error(exception,
			"Capability '{Kind}/{LocalId}' threw while handling '{Operation}'.",
			kind,
			localId,
			operation);

	public static void HostCancelDeliveryFailed(this ILogger logger, string correlationId, Exception exception)
		=> logger.Debug(exception,
			"Failed to deliver host.cancel for correlation '{CorrelationId}'.",
			correlationId);

	public static void IntegrationReinitializationFailed(this ILogger logger, string integrationId, Exception exception)
		=> logger.Warning(exception,
			"Integration '{IntegrationId}' failed to (re)initialize on a reconnect.",
			integrationId);

	public static void EventPublishFailed(this ILogger logger, string eventId, Exception exception)
		=> logger.Debug(exception, "Failed to publish event '{EventId}' to the host.", eventId);

	public static void HostCallbackFailed(this ILogger logger, string api, string operation, Exception exception)
		=> logger.Debug(exception, "Fire-and-forget host callback '{Api}/{Operation}' failed.", api, operation);

	public static void UiSessionCallFailed(this ILogger logger,
		string sessionId,
		string operation,
		Exception exception)
		=> logger.Warning(exception,
			"UI session '{SessionId}' failed during '{Operation}' and was reported to the host as faulted.",
			sessionId,
			operation);

	public static void AssetUploadFailed(this ILogger logger, string assetId, string kind, Exception exception)
		=> logger.Warning(exception, "Uploading asset '{AssetId}' of kind '{Kind}' failed.", assetId, kind);

	public static void AssetUploadRejected(this ILogger logger, string assetId, string step)
		=> logger.Debug("The host rejected asset '{AssetId}' at step '{Step}'.", assetId, step);

	public static void IconPublishFailed(this ILogger logger, Exception exception)
		=> logger.Warning(exception, "Publishing the integration icon failed.");

	public static void CatalogChangeNotifyFailed(this ILogger logger, string kind, Exception exception)
		=> logger.Debug(exception, "Failed to notify the host that the '{Kind}' catalogue changed.", kind);

	// None of the pairing templates below ever carry the verifier, the request id or the issued secret -
	// the whole point of pairing is that none of those need to appear anywhere a person could read them.
	public static void PairingRequested(this ILogger logger, string pluginId)
		=> logger.Information(
			"No stored credentials for '{PluginId}'. A pairing request was sent - approve or reject it " +
			"in the Macro Deck desktop app.",
			pluginId);

	public static void Paired(this ILogger logger, string pluginId)
		=> logger.Information("Paired with the host as '{PluginId}'.", pluginId);

	public static void PairingRejected(this ILogger logger, string pluginId)
		=> logger.Error("The pairing request for '{PluginId}' was rejected in the Macro Deck desktop app.", pluginId);

	public static void PairingExpired(this ILogger logger, string pluginId)
		=> logger.Error("The pairing request for '{PluginId}' expired before it was approved.", pluginId);

	public static void PairingDeveloperModeDisabled(this ILogger logger)
		=> logger.Warning("Developer Mode is disabled in Macro Deck, so no pairing request was sent. Enable it under " +
			"Settings > Developer; pairing is retried until then.");

	// The fallback's environment variable is named here rather than in the fault reason, because the
	// fault reason is served verbatim by the diagnostics endpoint, which must never name a credential.
	public static void HeadlessEnrollmentHint(this ILogger logger)
		=> logger.Error(
			"For headless or automated development, set MACRO_DECK_PLUGIN_ENROLLMENT_TOKEN to a Developer " +
			"token created in the Macro Deck desktop app instead of pairing interactively.");

	public static void HostProcessGone(this ILogger logger, int hostProcessId)
		=> logger.Warning("The host process {HostProcessId} that launched this plugin is no longer running; stopping.",
			hostProcessId);
}
