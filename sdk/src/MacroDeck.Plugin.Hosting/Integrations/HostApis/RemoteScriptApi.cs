using System.Text.Json;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Scripts;

namespace MacroDeck.Plugin.Hosting.Integrations.HostApis;

/// <summary>Proxies <see cref="IScriptApi"/> over <c>host.invoke</c> against <see cref="HostApis.Scripts"/>.
/// <see cref="GetScripts"/> is served from <see cref="HostStateCache"/>; <see cref="RunAsync"/> is a
/// real round trip, since it has to report the run's actual outcome.</summary>
internal sealed class RemoteScriptApi(IHostInvoker invoker, HostStateCache stateCache) : IScriptApi
{
	public IReadOnlyList<Script> GetScripts() => stateCache.GetList<Script>(Protocol.Callbacks.HostApis.Scripts);

	public async Task<ActionResult> RunAsync(
		string scriptId,
		IReadOnlyDictionary<string, object?>? inputs = null,
		string? originClientId = null,
		string? ownerWidgetId = null,
		CancellationToken cancellationToken = default)
	{
		var result = await invoker.InvokeAsync(Protocol.Callbacks.HostApis.Scripts,
			HostOperations.Scripts.Run,
			new ScriptsRunArguments
			{
				ScriptId = scriptId,
				OriginClientId = originClientId,
				Inputs = inputs,
				OwnerWidgetId = ownerWidgetId
			},
			cancellationToken);

		return result?.Deserialize<ActionResult>(PluginProtocolJson.Options) ??
			ActionResult.Failed(ActionErrorCodes.ProviderError, "The host returned no result.");
	}
}
