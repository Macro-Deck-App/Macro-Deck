using System.Text.Json;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.Actions;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Application.Plugins.Capabilities.Adapters.Actions;

public sealed class RemoteActionExecutor(
	IPluginCapabilityInvoker invoker,
	string pluginId,
	string localId) : IActionExecutor
{
	public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
	{
		var request = new CapabilityInvokeRequest
		{
			Kind = CapabilityKinds.Actions,
			LocalId = localId,
			Operation = CapabilityOperations.Actions.Execute,
			Arguments = new ActionExecuteArguments
			{
				Parameters = ToWireParameters(context.Parameters),
				OriginClientId = context.OriginClientId,
				OwnerWidgetId = context.OwnerWidgetId
			}
		};

		try
		{
			var data = await invoker.InvokeAsync(pluginId, request, context.CancellationToken).ConfigureAwait(false);
			var result = data?.Deserialize<ActionExecuteResult>(PluginProtocolJson.Options);

			return result is { Accepted: true }
				? result.ExpectedStateId is { Length: > 0 }
					? ActionResult.Accepted(result.Message ?? default, result.ExpectedStateId)
					: ActionResult.Accepted(result.Message ?? default)
				: result?.ExpectedStateId is { Length: > 0 }
					? ActionResult.Success(result.ExpectedStateId)
					: ActionResult.Success();
		}
		catch (RemoteCapabilityException exception)
		{
			return ActionResult.Failed(exception.Code, exception.Message);
		}
	}

	private static Dictionary<string, JsonElement> ToWireParameters(
		IReadOnlyDictionary<string, object> parameters)
	{
		var wire = new Dictionary<string, JsonElement>(parameters.Count, StringComparer.Ordinal);

		foreach (var (name, value) in parameters)
		{
			wire[name] = value is JsonElement element
				? element
				: JsonSerializer.SerializeToElement(value, PluginProtocolJson.Options);
		}

		return wire;
	}
}
