using MacroDeck.Plugin.Packaging.Manifest;
using MacroDeckHost.Application.Plugins.Runtime;

namespace MacroDeckHost.Application.Plugins;

public interface IPluginDesktopNotificationAccessPolicy
{
	bool IsAllowed(string pluginId);
}

public sealed class PluginDesktopNotificationAccessPolicy(
	IPluginSessionRegistry sessionRegistry,
	IPluginInstallationCatalog installationCatalog,
	IPluginManifestReader manifestReader) : IPluginDesktopNotificationAccessPolicy
{
	private readonly PluginPermissionDeclarations _declarations =
		new(PluginPermissions.HostDesktopNotifications, sessionRegistry, installationCatalog, manifestReader);

	public bool IsAllowed(string pluginId)
	{
		return _declarations.IsGrantable(pluginId);
	}
}
