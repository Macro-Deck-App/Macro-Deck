using System.Text.Json;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Errors;

namespace MacroDeckHost.Application.Plugins.Capabilities;

public interface IPluginCapabilityInvoker
{
	Task<JsonElement?> InvokeAsync(string pluginId,
		CapabilityInvokeRequest request,
		CancellationToken cancellationToken);

	bool TryComplete(string pluginId, ProtocolEnvelope result);

	void AbortAll(string pluginId, ProtocolError reason);

	bool IsLiveActionExecute(string pluginId, string correlationId);
}
