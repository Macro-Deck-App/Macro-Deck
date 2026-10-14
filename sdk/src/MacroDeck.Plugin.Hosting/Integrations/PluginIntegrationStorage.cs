using MacroDeck.Sdk.Storage;

namespace MacroDeck.Plugin.Hosting.Integrations;

internal static class PluginIntegrationStorage
{
	public const string FolderName = "integration-data";

	public static IIntegrationStorage Create(string? dataDirectory, string pluginId)
	{
		return string.IsNullOrEmpty(dataDirectory)
			? UnavailableIntegrationStorage.Instance
			: new DirectoryIntegrationStorage(Path.Combine(dataDirectory, FolderName, pluginId));
	}
}
