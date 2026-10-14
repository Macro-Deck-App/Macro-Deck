using System.Collections.Concurrent;
using MacroDeck.Plugin.Packaging.Manifest;
using MacroDeckHost.Application.Plugins.Runtime;

namespace MacroDeckHost.Application.Plugins;

// A self-registered session was admitted by the user through a developer token or pairing and declares nothing
// to the host, so an enforced permission counts as declared for it (ADR 0092, ADR 0110).
internal sealed class PluginPermissionDeclarations(
	string permission,
	IPluginSessionRegistry sessionRegistry,
	IPluginInstallationCatalog installationCatalog,
	IPluginManifestReader manifestReader)
{
	private readonly ConcurrentDictionary<string, bool> _declaresByManifest = new(StringComparer.Ordinal);

	public bool IsGrantable(string pluginId)
	{
		return IsSelfRegistered(pluginId) || Declares(pluginId);
	}

	private bool IsSelfRegistered(string pluginId)
	{
		return sessionRegistry.Snapshot()
			.Any(session => session.PluginId == pluginId
				&& session.State == PluginSessionState.Connected
				&& session.Origin == PluginSessionOrigin.SelfRegistered);
	}

	private bool Declares(string pluginId)
	{
		if (!installationCatalog.TryResolveActive(pluginId, out var active) || active is null)
		{
			return false;
		}

		var key = $"{active.ManifestPath}|{File.GetLastWriteTimeUtc(active.ManifestPath).Ticks}";
		if (_declaresByManifest.TryGetValue(key, out var declares))
		{
			return declares;
		}

		var manifest = manifestReader.Read(active.ManifestPath, pluginId, active.Version).Manifest;
		if (manifest is null)
		{
			return false;
		}

		declares = manifest.Permissions?.Contains(permission, StringComparer.Ordinal) == true;
		_declaresByManifest[key] = declares;
		return declares;
	}
}
