namespace MacroDeckHost.Application.Plugins;

public interface IPluginSessionTokenIssuer
{
	string Issue(string pluginId, string sessionId);
}
