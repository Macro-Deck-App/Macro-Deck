using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Plugins.Capabilities.Callbacks;

internal static class PluginCallbackRouterLog
{
	public static void CallbackFailed(
		ILogger logger,
		string api,
		string operation,
		string pluginId,
		Exception exception)
		=> logger.Error(exception,
			"host.invoke '{Api}/{Operation}' for plugin '{PluginId}' failed.",
			api,
			operation,
			pluginId);
}
