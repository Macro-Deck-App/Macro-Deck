using System.Text.Json;
using MacroDeck.Plugin.Hosting.Capabilities.WidgetTypeProvider;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Sdk.Widgets;

namespace MacroDeck.Plugin.Hosting.Integrations.HostApis;

/// <summary>
/// Proxies <see cref="IWidgetTypeProviderContext" /> over <c>host.invoke</c> against
/// <see cref="HostApis.WidgetTypes" />. Every call is a real round trip: a registration is what makes the
/// type exist for the host, so there is nothing meaningful to serve from a cache.
/// </summary>
internal sealed class RemoteWidgetTypeProviderContext(IHostInvoker invoker) : IWidgetTypeProviderContext
{
	public async Task<WidgetTypeRegistration> RegisterWidgetTypeAsync(
		WidgetTypeDescriptor widgetType,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(widgetType);

		var result = await invoker.InvokeAsync(Protocol.Callbacks.HostApis.WidgetTypes,
			HostOperations.WidgetTypes.Register,
			new WidgetTypesRegisterArguments { WidgetType = WidgetTypeDescriptorMapper.ToDto(widgetType) },
			cancellationToken);

		var registered = result?.Deserialize<WidgetTypesRegisterResult>(PluginProtocolJson.Options);

		return registered is null
			? new WidgetTypeRegistration(string.Empty, widgetType.Id)
			: new WidgetTypeRegistration(registered.WidgetTypeId, registered.ProviderId);
	}

	public Task UnregisterWidgetTypeAsync(string widgetTypeId, CancellationToken cancellationToken = default)
		=> invoker.InvokeAsync(Protocol.Callbacks.HostApis.WidgetTypes,
			HostOperations.WidgetTypes.Unregister,
			new WidgetTypesUnregisterArguments { WidgetTypeId = widgetTypeId },
			cancellationToken);
}
