namespace MacroDeckHost.Application.Plugins.Capabilities;

public interface IRemotePluginConnectionState
{
	bool IsConnected(string pluginId);
}
