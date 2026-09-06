namespace MacroDeckHost.Application.Plugins.Capabilities;

public interface IRemotePluginSnapshotStore
{
	RemotePluginCapabilitySnapshot GetSnapshot(string pluginId);

	bool Has(string pluginId);

	Task SaveAsync(RemotePluginCapabilitySnapshot snapshot, CancellationToken cancellationToken = default);
}
