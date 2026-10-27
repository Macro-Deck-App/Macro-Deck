namespace MacroDeckHost.Application.Plugins.Capabilities;

public interface IRemotePluginSnapshotStore
{
	RemotePluginCapabilitySnapshot GetSnapshot(string pluginId);

	bool Has(string pluginId);

	Task SaveAsync(RemotePluginCapabilitySnapshot snapshot, CancellationToken cancellationToken = default);

	Task RemoveAsync(string pluginId, CancellationToken cancellationToken = default);
}
