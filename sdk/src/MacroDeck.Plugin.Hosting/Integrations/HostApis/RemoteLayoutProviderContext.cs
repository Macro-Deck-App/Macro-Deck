using System.Text.Json;
using MacroDeck.Plugin.Hosting.Capabilities.LayoutProvider;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Sdk.Layouts;

namespace MacroDeck.Plugin.Hosting.Integrations.HostApis;

/// <summary>
/// Proxies <see cref="ILayoutProviderContext" /> over <c>host.invoke</c> against
/// <see cref="HostApis.Layouts" />. Every call is a real round trip: a registration is what makes the
/// layout exist for the host, so there is nothing meaningful to serve from a cache.
/// </summary>
internal sealed class RemoteLayoutProviderContext(IHostInvoker invoker) : ILayoutProviderContext
{
	public async Task<LayoutRegistration> RegisterLayoutAsync(
		LayoutDescriptor layout,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(layout);

		var result = await invoker.InvokeAsync(Protocol.Callbacks.HostApis.Layouts,
			HostOperations.Layouts.Register,
			new LayoutsRegisterArguments { Layout = LayoutDescriptorMapper.ToDto(layout) },
			cancellationToken);

		var registered = result?.Deserialize<LayoutsRegisterResult>(PluginProtocolJson.Options);

		return registered is null
			? new LayoutRegistration(string.Empty, layout.Id)
			: new LayoutRegistration(registered.LayoutId, registered.ProviderId);
	}

	public Task UnregisterLayoutAsync(string layoutId, CancellationToken cancellationToken = default)
		=> invoker.InvokeAsync(Protocol.Callbacks.HostApis.Layouts,
			HostOperations.Layouts.Unregister,
			new LayoutsUnregisterArguments { LayoutId = layoutId },
			cancellationToken);
}
