using System.Collections.Concurrent;
using MacroDeckHost.Application.Plugins;
using SdkHistoryWindow = MacroDeck.Sdk.Variables.IVariableHistoryWindow;

namespace MacroDeckHost.Application.Variables;

public sealed class PluginVariableHistoryWindows : IDisposable
{
	private readonly OwnedVariableHistory _history;
	private readonly IPluginSessionRegistry _sessions;
	private readonly ConcurrentDictionary<(string PluginId, string WindowId), SdkHistoryWindow> _windows = new();

	public PluginVariableHistoryWindows(OwnedVariableHistory history, IPluginSessionRegistry sessions)
	{
		_history = history;
		_sessions = sessions;
		_sessions.SessionEnded += OnSessionEnded;
	}

	public string Open(string pluginId, string name, int capacity)
	{
		var window = _history.Open(pluginId, name, capacity);
		var windowId = Guid.NewGuid().ToString("N");
		_windows[(pluginId, windowId)] = window;
		return windowId;
	}

	public int OpenCount(string pluginId)
	{
		return _windows.Count(pair => string.Equals(pair.Key.PluginId, pluginId, StringComparison.Ordinal));
	}

	public IReadOnlyList<double?>? Read(string pluginId, string windowId)
	{
		return _windows.TryGetValue((pluginId, windowId), out var window) ? window.Values : null;
	}

	public async Task CloseAsync(string pluginId, string windowId)
	{
		if (_windows.TryRemove((pluginId, windowId), out var window))
		{
			await window.DisposeAsync().ConfigureAwait(false);
		}
	}

	public void Dispose()
	{
		_sessions.SessionEnded -= OnSessionEnded;

		foreach (var key in _windows.Keys)
		{
			if (_windows.TryRemove(key, out var window))
			{
				_ = window.DisposeAsync().AsTask();
			}
		}
	}

	private void OnSessionEnded(object? sender, PluginSessionEndedEventArgs args)
	{
		foreach (var key in _windows.Keys.Where(key
			=> string.Equals(key.PluginId, args.PluginId, StringComparison.Ordinal)))
		{
			if (_windows.TryRemove(key, out var window))
			{
				_ = window.DisposeAsync().AsTask();
			}
		}
	}
}
