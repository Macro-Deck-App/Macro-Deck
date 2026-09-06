using System.Text.Json;
using MacroDeck.Plugin.Hosting.Capabilities.FolderViewProvider;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Sdk.FolderViews;

namespace MacroDeck.Plugin.Hosting.Integrations.HostApis;

/// <summary>
/// Proxies <see cref="IFolderViewProviderContext" /> over <c>host.invoke</c> against
/// <see cref="HostApis.FolderViews" />. Every call is a real round trip: a registration is what makes the
/// view exist for the host, so there is nothing meaningful to serve from a cache.
/// </summary>
internal sealed class RemoteFolderViewProviderContext(IHostInvoker invoker) : IFolderViewProviderContext
{
	public async Task<FolderViewRegistration> RegisterFolderViewAsync(
		FolderViewDescriptor folderView,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(folderView);

		var result = await invoker.InvokeAsync(Protocol.Callbacks.HostApis.FolderViews,
			HostOperations.FolderViews.Register,
			new FolderViewsRegisterArguments { FolderView = FolderViewDescriptorMapper.ToDto(folderView) },
			cancellationToken);

		var registered = result?.Deserialize<FolderViewsRegisterResult>(PluginProtocolJson.Options);

		return registered is null
			? new FolderViewRegistration(string.Empty, folderView.Id)
			: new FolderViewRegistration(registered.FolderViewId, registered.ProviderId);
	}

	public Task UnregisterFolderViewAsync(string folderViewId, CancellationToken cancellationToken = default)
		=> invoker.InvokeAsync(Protocol.Callbacks.HostApis.FolderViews,
			HostOperations.FolderViews.Unregister,
			new FolderViewsUnregisterArguments { FolderViewId = folderViewId },
			cancellationToken);
}
