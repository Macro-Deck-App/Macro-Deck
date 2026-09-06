namespace MacroDeckHost.Application.Plugins;

public sealed class PluginRegistrationConflictException : Exception
{
	public string PluginId { get; }

	public PluginRegistrationConflictException(string pluginId, Exception? innerException = null)
		: base($"A plugin registration for '{pluginId}' already exists.", innerException)
	{
		PluginId = pluginId;
	}
}
