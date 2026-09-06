using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Application.Plugins.Capabilities.Adapters.Actions;

// The four {dynamic-options} x {state} leaves. Each is a thin forward to the base class's *Core members
// - see RemoteActionDefinition's remarks for why the interfaces cannot be merged into one class that
// implements everything unconditionally.

internal sealed class RemoteActionDefinitionPlain(
	IPluginCapabilityInvoker invoker,
	string pluginId,
	RemoteActionDescriptor descriptor) : RemoteActionDefinition(invoker, pluginId, descriptor);

internal sealed class RemoteActionDefinitionWithDynamicOptions(
	IPluginCapabilityInvoker invoker,
	string pluginId,
	RemoteActionDescriptor descriptor)
	: RemoteActionDefinition(invoker, pluginId, descriptor), IDynamicOptionsActionDefinition
{
	public Task<DynamicOptionsResult> GetDynamicOptionsAsync(DynamicOptionsContext context,
		CancellationToken cancellationToken)
		=> GetDynamicOptionsCoreAsync(context, cancellationToken);
}

internal sealed class RemoteActionDefinitionWithState(
	IPluginCapabilityInvoker invoker,
	string pluginId,
	RemoteActionDescriptor descriptor)
	: RemoteActionDefinition(invoker, pluginId, descriptor), IStateProviderActionDefinition
{
	public Task<ActionStateSnapshot?> GetActionStateAsync(
		IReadOnlyDictionary<string, object?> parameters,
		CancellationToken cancellationToken)
		=> GetActionStateCoreAsync(parameters, cancellationToken);
}

internal sealed class RemoteActionDefinitionWithDynamicOptionsAndState(
	IPluginCapabilityInvoker invoker,
	string pluginId,
	RemoteActionDescriptor descriptor)
	: RemoteActionDefinition(invoker, pluginId, descriptor), IDynamicOptionsActionDefinition,
		IStateProviderActionDefinition
{
	public Task<DynamicOptionsResult> GetDynamicOptionsAsync(DynamicOptionsContext context,
		CancellationToken cancellationToken)
		=> GetDynamicOptionsCoreAsync(context, cancellationToken);

	public Task<ActionStateSnapshot?> GetActionStateAsync(
		IReadOnlyDictionary<string, object?> parameters,
		CancellationToken cancellationToken)
		=> GetActionStateCoreAsync(parameters, cancellationToken);
}
