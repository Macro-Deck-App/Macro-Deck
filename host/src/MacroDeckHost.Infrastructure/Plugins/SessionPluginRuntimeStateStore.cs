using MacroDeckHost.Application.Plugins.Runtime;

namespace MacroDeckHost.Infrastructure.Plugins;

public sealed class SessionPluginRuntimeStateStore : IPluginRuntimeStateStore
{
	private readonly IPluginRuntimeStateStore _persistent;
	private readonly object _lock = new();
	private readonly Dictionary<string, bool> _states = new(StringComparer.Ordinal);

	public SessionPluginRuntimeStateStore(IPluginRuntimeStateStore persistent)
	{
		_persistent = persistent;
	}

	public IReadOnlyDictionary<string, bool> Load()
	{
		lock (_lock)
		{
			return new Dictionary<string, bool>(_states, StringComparer.Ordinal);
		}
	}

	public Task Save(string pluginId, bool started)
	{
		lock (_lock)
		{
			_states[pluginId] = started;
		}

		return Task.CompletedTask;
	}

	public Task Remove(string pluginId)
	{
		lock (_lock)
		{
			_states.Remove(pluginId);
		}

		return _persistent.Remove(pluginId);
	}
}
