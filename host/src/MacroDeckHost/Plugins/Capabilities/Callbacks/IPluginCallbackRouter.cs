using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeckHost.Application.Plugins;

namespace MacroDeckHost.Plugins.Capabilities.Callbacks;

public interface IPluginCallbackRouter
{
	Task<HostCallbackResult> RouteAsync(
		string pluginId,
		string correlationId,
		HostInvokePayload payload,
		CancellationToken cancellationToken);

	Task<HostCallbackResult> RouteAsync(
		string pluginId,
		string? sessionId,
		string correlationId,
		HostInvokePayload payload,
		CancellationToken cancellationToken);

	Task<HostCallbackResult> RouteAsync(
		string pluginId,
		string? sessionId,
		IPluginConnection? connection,
		string correlationId,
		HostInvokePayload payload,
		CancellationToken cancellationToken)
		=> RouteAsync(pluginId, sessionId, correlationId, payload, cancellationToken);

	HostCallbackResult? Admit(string pluginId, HostInvokePayload payload);
}
