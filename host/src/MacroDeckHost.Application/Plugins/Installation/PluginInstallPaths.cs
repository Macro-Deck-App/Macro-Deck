using MacroDeck.Plugin.Packaging.Artifacts;

namespace MacroDeckHost.Application.Plugins.Installation;

public static class PluginInstallPaths
{
	public static string PluginDirectory(string pluginsDirectory, string pluginId)
	{
		return Path.Combine(pluginsDirectory, pluginId);
	}

	public static string VersionsDirectory(string pluginsDirectory, string pluginId)
	{
		return Path.Combine(PluginDirectory(pluginsDirectory, pluginId),
			PluginArtifactFiles.VersionsDirectoryName);
	}

	public static string VersionDirectory(string pluginsDirectory, string pluginId, string version)
	{
		return Path.Combine(VersionsDirectory(pluginsDirectory, pluginId), version);
	}

	public static string DataDirectory(string pluginsDirectory, string pluginId)
	{
		return Path.Combine(PluginDirectory(pluginsDirectory, pluginId),
			PluginArtifactFiles.DataDirectoryName);
	}

	public static string CurrentFilePath(string pluginsDirectory, string pluginId)
	{
		return Path.Combine(PluginDirectory(pluginsDirectory, pluginId),
			PluginArtifactFiles.CurrentFileName);
	}

	public static string ManifestPath(string pluginsDirectory, string pluginId, string version)
	{
		return Path.Combine(VersionDirectory(pluginsDirectory, pluginId, version),
			PluginArtifactFiles.ManifestFileName);
	}
}
