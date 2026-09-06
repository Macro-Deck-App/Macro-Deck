using System.Text.Json;
using MacroDeck.Plugin.Protocol.Assets;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.Actions;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeckHost.Application.Plugins.Assets;
using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Application.Plugins.Capabilities.Adapters.Actions;

// Serves one remote action's icon-provider operations over capability.invoke. Deliberately not one of
// RemoteActionDefinition's leaves - see RemoteIconProviderActionRegistry's class remarks - so this holds
// its own (pluginId, localId) pair rather than a RemoteActionDescriptor.
public sealed class RemoteIconProviderAction : IIconProviderActionDefinition
{
	private readonly IPluginCapabilityInvoker _invoker;
	private readonly IPluginAssetCache _assetCache;

	public RemoteIconProviderAction(string pluginId,
		string localId,
		IPluginCapabilityInvoker invoker,
		IPluginAssetCache assetCache)
	{
		PluginId = pluginId;
		LocalId = localId;
		_invoker = invoker;
		_assetCache = assetCache;
	}

	public string PluginId { get; }

	public string LocalId { get; }

	public async Task<ActionIconSnapshot?> GetActionIconAsync(
		IReadOnlyDictionary<string, object?> parameters,
		CancellationToken cancellationToken)
	{
		var data = await InvokeAsync(CapabilityOperations.Actions.Icon,
			new ActionExecuteArguments { Parameters = ToWireParameters(parameters) },
			cancellationToken).ConfigureAwait(false);

		var result = data?.Deserialize<ActionIconResult>(PluginProtocolJson.Options);

		return result is { HasValue: true }
			? new ActionIconSnapshot
			{
				Version = result.Version,
				Reference = result.Reference is { } reference
					? new ActionIconReference(reference.Type, reference.Reference)
					: null,
				MediaType = result.MediaType,
				NoIcon = result.NoIcon
			}
			: null;
	}

	public async Task<ActionIconContent?> GetActionIconContentAsync(
		IReadOnlyDictionary<string, object?> parameters,
		string version,
		CancellationToken cancellationToken)
	{
		var data = await InvokeAsync(CapabilityOperations.Actions.IconContent,
			new ActionIconContentArguments { Parameters = ToWireParameters(parameters), Version = version },
			cancellationToken).ConfigureAwait(false);

		var result = data?.Deserialize<ActionIconContentResult>(PluginProtocolJson.Options);
		if (result is not { HasValue: true })
		{
			return null;
		}

		return result.ContentHash is { } contentHash &&
			AssetContentHash.IsValid(contentHash) &&
			_assetCache.TryRead(contentHash, out var bytes, out var mimeType)
				? new ActionIconContent(bytes, mimeType)
				: null;
	}

	private Task<JsonElement?> InvokeAsync(string operation, object arguments, CancellationToken cancellationToken)
		=> _invoker.InvokeAsync(PluginId,
			new CapabilityInvokeRequest
			{
				Kind = CapabilityKinds.Actions, LocalId = LocalId, Operation = operation, Arguments = arguments
			},
			cancellationToken);

	private static Dictionary<string, JsonElement> ToWireParameters(
		IReadOnlyDictionary<string, object?> parameters)
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
