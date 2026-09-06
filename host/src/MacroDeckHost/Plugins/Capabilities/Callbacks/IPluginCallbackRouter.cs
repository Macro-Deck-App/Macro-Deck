using MacroDeck.Plugin.Protocol.Callbacks;

namespace MacroDeckHost.Plugins.Capabilities.Callbacks;

public interface IPluginCallbackRouter
{
	Task<HostCallbackResult> RouteAsync(
		string pluginId,
		string correlationId,
		HostInvokePayload payload,
		CancellationToken cancellationToken);
}
