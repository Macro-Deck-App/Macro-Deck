namespace MacroDeckHost.Application.Plugins.IconPacks;

public enum PluginIconPackSyncStatus
{
	Synced,
	Skipped,
	Invalid
}

public sealed record PluginIconPackSyncResult(PluginIconPackSyncStatus Status, bool Changed = false, IReadOnlyList<string>? InvalidKeys = null)
{
	public static readonly PluginIconPackSyncResult Skipped = new(PluginIconPackSyncStatus.Skipped);
}

public interface IPluginIconPackSync
{
	Task<PluginIconPackSyncResult> SyncAsync(string pluginId, CancellationToken cancellationToken);

	void InstallationChanged(string pluginId);

	Task<PluginIconPackSyncResult> SyncDevelopmentAsync(string pluginId,
		string sessionId,
		string pluginName,
		IReadOnlyList<DevelopmentIconPack> packs,
		CancellationToken cancellationToken);
}
