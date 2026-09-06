namespace MacroDeckHost.Application.Plugins.Capabilities;

public sealed class PluginSessionConnectionState(IPluginSessionRegistry sessionRegistry) : IRemotePluginConnectionState
{
	public bool IsConnected(string pluginId)
		=> sessionRegistry.Snapshot()
			.Any(session => session.State == PluginSessionState.Connected &&
				string.Equals(session.PluginId, pluginId, StringComparison.Ordinal));
}
