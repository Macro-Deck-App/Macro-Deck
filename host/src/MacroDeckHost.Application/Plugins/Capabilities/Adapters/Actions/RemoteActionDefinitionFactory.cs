namespace MacroDeckHost.Application.Plugins.Capabilities.Adapters.Actions;

public static class RemoteActionDefinitionFactory
{
	public static RemoteActionDefinition Create(
		IPluginCapabilityInvoker invoker,
		string pluginId,
		RemoteActionDescriptor descriptor)
	{
		var hasDynamicOptions = descriptor.SupportsDynamicOptions;
		var providesState = descriptor.ProvidesState;

		return (hasDynamicOptions, providesState) switch
		{
			(false, false) => new RemoteActionDefinitionPlain(invoker, pluginId, descriptor),
			(true, false) => new RemoteActionDefinitionWithDynamicOptions(invoker, pluginId, descriptor),
			(false, true) => new RemoteActionDefinitionWithState(invoker, pluginId, descriptor),
			(true, true) => new RemoteActionDefinitionWithDynamicOptionsAndState(invoker, pluginId, descriptor)
		};
	}
}
