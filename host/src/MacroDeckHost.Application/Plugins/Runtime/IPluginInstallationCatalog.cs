namespace MacroDeckHost.Application.Plugins.Runtime;

public sealed record InstalledPluginVersion
{
	public required string Version { get; init; }

	public required string VersionDirectory { get; init; }

	public required string ManifestPath { get; init; }
}

public sealed record InstalledPlugin
{
	public required string PluginId { get; init; }

	public required string PluginDirectory { get; init; }

	public required IReadOnlyList<InstalledPluginVersion> Versions { get; init; }

	public InstalledPluginVersion? ActiveVersion { get; init; }
}

public interface IPluginInstallationCatalog
{
	IReadOnlyList<InstalledPlugin> Discover();

	bool TryResolveActive(string pluginId, out InstalledPluginVersion? version);

	void Invalidate();
}
