using MacroDeck.Sdk.FolderViews;
using MacroDeckHost.Application.FolderViews;

namespace MacroDeckHost.Infrastructure.Integrations;

/// <summary>
/// Binds one in-process integration to the folder view registry. The integration id is captured here, so
/// a provider can only ever register views in its own name.
/// </summary>
internal sealed class IntegrationFolderViewProviderContext : IFolderViewProviderContext
{
	private readonly string _integrationId;
	private readonly IFolderViewRegistry _registry;

	public IntegrationFolderViewProviderContext(string integrationId, IFolderViewRegistry registry)
	{
		_integrationId = integrationId;
		_registry = registry;
	}

	public Task<FolderViewRegistration> RegisterFolderViewAsync(
		FolderViewDescriptor folderView,
		CancellationToken cancellationToken = default)
		=> _registry.Register(_integrationId, folderView, cancellationToken);

	public Task UnregisterFolderViewAsync(string folderViewId, CancellationToken cancellationToken = default)
		=> _registry.Unregister(_integrationId, folderViewId, cancellationToken);
}
