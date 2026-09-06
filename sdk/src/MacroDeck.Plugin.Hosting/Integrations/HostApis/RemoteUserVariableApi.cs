using System.Text.Json;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Sdk.Variables;

namespace MacroDeck.Plugin.Hosting.Integrations.HostApis;

/// <summary>Proxies <see cref="IUserVariableApi"/> over <c>host.invoke</c> against
/// <see cref="HostApis.UserVariables"/>.</summary>
internal sealed class RemoteUserVariableApi(IHostInvoker invoker) : IUserVariableApi
{
	public async Task<UserVariableWriteResult> ApplyAsync(
		string name,
		string? ownerWidgetId,
		UserVariableOperation operation,
		string? value,
		CancellationToken cancellationToken = default)
	{
		var result = await invoker.InvokeAsync(Protocol.Callbacks.HostApis.UserVariables,
			HostOperations.UserVariables.Apply,
			new UserVariablesApplyArguments
			{
				Name = name, OwnerWidgetId = ownerWidgetId, Operation = operation.ToString(), Value = value
			},
			cancellationToken);

		return result?.Deserialize<UserVariableWriteResult>(PluginProtocolJson.Options) ??
			new UserVariableWriteResult(UserVariableWriteStatus.NotFound, "The host returned no result.");
	}

	public async Task<UserVariableCreateResult> CreateAsync(
		string name,
		string? ownerWidgetId,
		VariableType type,
		string? initialValue = null,
		int? decimalPlaces = null,
		CancellationToken cancellationToken = default)
	{
		var result = await invoker.InvokeAsync(Protocol.Callbacks.HostApis.UserVariables,
			HostOperations.UserVariables.Create,
			new UserVariablesCreateArguments
			{
				Name = name,
				OwnerWidgetId = ownerWidgetId,
				Type = type.ToString(),
				InitialValue = initialValue,
				DecimalPlaces = decimalPlaces
			},
			cancellationToken);

		// Not NotSupported: this host does implement creation, the answer just never arrived.
		return result?.Deserialize<UserVariableCreateResult>(PluginProtocolJson.Options) ??
			UserVariableCreateResult.Failed(UserVariableCreateStatus.Unavailable,
				"The host returned no result.");
	}
}
