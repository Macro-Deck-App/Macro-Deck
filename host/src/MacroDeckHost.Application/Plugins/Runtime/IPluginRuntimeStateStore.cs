namespace MacroDeckHost.Application.Plugins.Runtime;

public interface IPluginRuntimeStateStore
{
	IReadOnlyDictionary<string, bool> Load();

	Task Save(string pluginId, bool started);

	Task Remove(string pluginId);
}
