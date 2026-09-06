using System.Text.Json;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Sdk.Variables;

namespace MacroDeck.Plugin.Hosting.Integrations.HostApis;

/// <summary>Proxies <see cref="IVariableApi"/> over <c>host.invoke</c> against <see cref="HostApis.Variables"/>.
/// Every member is a request/response round trip - the host owns the variable store, so there is
/// nothing to cache here.</summary>
internal sealed class RemoteVariableApi(IHostInvoker invoker) : IVariableApi
{
	public async Task<IReadOnlyList<VariableHandle>> GetAllAsync()
	{
		var result = await invoker.InvokeAsync(Protocol.Callbacks.HostApis.Variables,
			HostOperations.Variables.List,
			null,
			CancellationToken.None);
		return result?.Deserialize<List<VariableHandle>>(PluginProtocolJson.Options) ?? [];
	}

	public async Task<VariableHandle?> GetByNameAsync(string name)
	{
		var result = await invoker.InvokeAsync(Protocol.Callbacks.HostApis.Variables,
			HostOperations.Variables.Get,
			new VariablesGetArguments { Name = name },
			CancellationToken.None);
		return result?.Deserialize<VariableHandle?>(PluginProtocolJson.Options);
	}

	public async Task<VariableHandle> CreateAsync(
		string name,
		VariableType type,
		object? initialValue = null,
		int? decimalPlaces = null,
		string? definitionId = null)
	{
		var result = await invoker.InvokeAsync(Protocol.Callbacks.HostApis.Variables,
			HostOperations.Variables.Create,
			new VariablesCreateArguments
			{
				Name = name,
				Type = type.ToString(),
				InitialValue = initialValue is null
					? null
					: JsonSerializer.SerializeToElement(initialValue, PluginProtocolJson.Options),
				DecimalPlaces = decimalPlaces,
				DefinitionId = definitionId
			},
			CancellationToken.None);

		return result?.Deserialize<VariableHandle>(PluginProtocolJson.Options) ??
			throw new InvalidOperationException($"The host returned no variable for '{name}'.");
	}

	public Task SetValueAsync(Guid variableId, object? value)
		=> invoker.InvokeAsync(Protocol.Callbacks.HostApis.Variables,
			HostOperations.Variables.Set,
			new VariablesSetArguments
			{
				VariableId = variableId,
				Value = value is null ? null : JsonSerializer.SerializeToElement(value, PluginProtocolJson.Options)
			},
			CancellationToken.None);

	public Task DeleteAsync(Guid variableId)
		=> invoker.InvokeAsync(Protocol.Callbacks.HostApis.Variables,
			HostOperations.Variables.Delete,
			new VariablesDeleteArguments { VariableId = variableId },
			CancellationToken.None);
}
