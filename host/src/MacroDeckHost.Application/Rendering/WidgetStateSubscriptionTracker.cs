namespace MacroDeckHost.Application.Rendering;

public sealed class WidgetStateSubscriptionTracker
{
	private readonly object _lock = new();
	private readonly Dictionary<string, HashSet<string>> _connectionsByWidget = new(StringComparer.Ordinal);
	private readonly Dictionary<string, HashSet<string>> _widgetsByConnection = new(StringComparer.Ordinal);

	public void Add(string connectionId, string widgetId)
	{
		lock (_lock)
		{
			(_connectionsByWidget.TryGetValue(widgetId, out var conns)
				? conns
				: _connectionsByWidget[widgetId] = new(StringComparer.Ordinal)).Add(connectionId);
			(_widgetsByConnection.TryGetValue(connectionId, out var widgets)
				? widgets
				: _widgetsByConnection[connectionId] = new(StringComparer.Ordinal)).Add(widgetId);
		}
	}

	public void Remove(string connectionId, string widgetId)
	{
		lock (_lock)
		{
			RemovePair(connectionId, widgetId);
		}
	}

	public void RemoveConnection(string connectionId)
	{
		lock (_lock)
		{
			if (!_widgetsByConnection.TryGetValue(connectionId, out var widgets))
			{
				return;
			}

			foreach (var widgetId in widgets.ToArray())
			{
				RemovePair(connectionId, widgetId);
			}
		}
	}

	public bool HasSubscribers(string widgetId)
	{
		lock (_lock)
		{
			return _connectionsByWidget.TryGetValue(widgetId, out var conns) && conns.Count > 0;
		}
	}

	private void RemovePair(string connectionId, string widgetId)
	{
		if (_connectionsByWidget.TryGetValue(widgetId, out var conns))
		{
			conns.Remove(connectionId);
			if (conns.Count == 0)
			{
				_connectionsByWidget.Remove(widgetId);
			}
		}

		if (_widgetsByConnection.TryGetValue(connectionId, out var widgets))
		{
			widgets.Remove(widgetId);
			if (widgets.Count == 0)
			{
				_widgetsByConnection.Remove(connectionId);
			}
		}
	}
}
