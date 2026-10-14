using MacroDeck.Plugin.Protocol.Callbacks;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Plugins.Capabilities.Callbacks;

public sealed class PluginAdbInvokeRunner(
	IPluginCallbackRouter router,
	PluginAdbCallbacks callbacks,
	ILogger logger)
	: PluginBackgroundInvokeRunner(router, logger.ForContext<PluginAdbInvokeRunner>(), "adb")
{
	protected override Task<HostCallbackResult?> AdmitAsync(
		string pluginId,
		HostInvokePayload payload,
		CancellationToken cancellationToken)
	{
		return callbacks.AdmitAsync(pluginId, payload, cancellationToken);
	}

	protected override Task<HostCallbackResult> ExecuteAsync(
		string pluginId,
		HostInvokePayload payload,
		CancellationToken cancellationToken)
	{
		return callbacks.ExecuteAsync(pluginId, payload, cancellationToken);
	}
}
