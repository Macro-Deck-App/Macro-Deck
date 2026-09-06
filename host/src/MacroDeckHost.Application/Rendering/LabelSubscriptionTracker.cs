namespace MacroDeckHost.Application.Rendering;

public sealed class LabelSubscriptionTracker
{
	private readonly object _lock = new();

	private readonly Dictionary<string, Dictionary<string, HashSet<string>>> _connectionsByWidgetState =
		new(StringComparer.Ordinal);

	private readonly Dictionary<string, HashSet<(string WidgetId, string State)>> _keysByConnection
		= new(StringComparer.Ordinal);

	public void Add(string connectionId, string widgetId, string state)
	{
		state = LabelGroups.Normalize(state);
		lock (_lock)
		{
			var states = _connectionsByWidgetState.TryGetValue(widgetId, out var existingStates)
				? existingStates
				: _connectionsByWidgetState[widgetId] = new(StringComparer.Ordinal);
			var connections = states.TryGetValue(state, out var existingConnections)
				? existingConnections
				: states[state] = new(StringComparer.Ordinal);
			connections.Add(connectionId);

			var keys = _keysByConnection.TryGetValue(connectionId, out var existingKeys)
				? existingKeys
				: _keysByConnection[connectionId] = [];
			keys.Add((widgetId, state));
		}
	}

	public void Remove(string connectionId, string widgetId, string state)
	{
		state = LabelGroups.Normalize(state);
		lock (_lock)
		{
			RemovePair(connectionId, widgetId, state);
		}
	}

	public void RemoveConnection(string connectionId)
	{
		lock (_lock)
		{
			if (!_keysByConnection.TryGetValue(connectionId, out var keys))
			{
				return;
			}

			foreach (var (widgetId, state) in keys.ToArray())
			{
				RemovePair(connectionId, widgetId, state);
			}
		}
	}

	public bool HasSubscribers(string widgetId, string state)
	{
		state = LabelGroups.Normalize(state);
		lock (_lock)
		{
			return _connectionsByWidgetState.TryGetValue(widgetId, out var states) &&
				states.TryGetValue(state, out var connections) &&
				connections.Count > 0;
		}
	}

	public bool HasAnySubscribers(string widgetId)
	{
		lock (_lock)
		{
			return _connectionsByWidgetState.TryGetValue(widgetId, out var states) && states.Count > 0;
		}
	}

	/// <summary>The state ids of <paramref name="widgetId" /> that currently have at least one subscriber.</summary>
	public IReadOnlyCollection<string> SubscribedStates(string widgetId)
	{
		lock (_lock)
		{
			return _connectionsByWidgetState.TryGetValue(widgetId, out var states) ? states.Keys.ToArray() : [];
		}
	}

	private void RemovePair(string connectionId, string widgetId, string state)
	{
		if (_connectionsByWidgetState.TryGetValue(widgetId, out var states) &&
			states.TryGetValue(state, out var connections))
		{
			connections.Remove(connectionId);
			if (connections.Count == 0)
			{
				states.Remove(state);
				if (states.Count == 0)
				{
					_connectionsByWidgetState.Remove(widgetId);
				}
			}
		}

		if (_keysByConnection.TryGetValue(connectionId, out var keys))
		{
			keys.Remove((widgetId, state));
			if (keys.Count == 0)
			{
				_keysByConnection.Remove(connectionId);
			}
		}
	}
}
