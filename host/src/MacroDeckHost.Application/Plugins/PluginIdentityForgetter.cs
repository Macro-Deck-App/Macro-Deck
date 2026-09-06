using MacroDeckHost.Application.Plugins.Compatibility;
using MacroDeckHost.Application.Plugins.Logging;

namespace MacroDeckHost.Application.Plugins;

public interface IPluginIdentityForgetter
{
	void Forget(string pluginId);
}

public sealed class PluginIdentityForgetter : IPluginIdentityForgetter
{
	private readonly IPluginCompatibilityService _compatibility;
	private readonly IPluginLogRateLimiter _logRateLimiter;
	private readonly IPluginLogIngestor _logIngestor;

	public PluginIdentityForgetter(
		IPluginCompatibilityService compatibility,
		IPluginLogRateLimiter logRateLimiter,
		IPluginLogIngestor logIngestor)
	{
		_compatibility = compatibility;
		_logRateLimiter = logRateLimiter;
		_logIngestor = logIngestor;
	}

	public void Forget(string pluginId)
	{
		_compatibility.Clear(pluginId);

		_logRateLimiter.Evict(pluginId);
		_logIngestor.Evict(pluginId);
	}
}
