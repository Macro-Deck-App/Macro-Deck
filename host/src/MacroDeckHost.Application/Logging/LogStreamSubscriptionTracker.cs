namespace MacroDeckHost.Application.Logging;

public sealed class LogStreamSubscriptionTracker
{
	private readonly object _lock = new();
	private readonly Dictionary<string, LogSubscription> _connections = new(StringComparer.Ordinal);

	public bool HasSubscribers
	{
		get
		{
			lock (_lock)
			{
				return _connections.Count > 0;
			}
		}
	}

	public void Set(string connectionId, LogQuery query, LogCursor position)
	{
		lock (_lock)
		{
			_connections[connectionId] = new LogSubscription(query, position);
		}
	}

	public void Remove(string connectionId)
	{
		lock (_lock)
		{
			_connections.Remove(connectionId);
		}
	}

	public IReadOnlyList<KeyValuePair<string, LogSubscription>> Snapshot()
	{
		lock (_lock)
		{
			return _connections.ToList();
		}
	}

	public void Advance(string connectionId, LogCursor from, LogCursor to)
	{
		lock (_lock)
		{
			if (_connections.TryGetValue(connectionId, out var existing) && existing.Position == from)
			{
				_connections[connectionId] = existing with { Position = to };
			}
		}
	}

	public LogCursor? MinimumPosition()
	{
		lock (_lock)
		{
			if (_connections.Count == 0)
			{
				return null;
			}

			LogCursor? minimum = null;
			foreach (var subscription in _connections.Values)
			{
				minimum = minimum is not { } current
					? subscription.Position
					: new LogCursor(Min(current.Host, subscription.Position.Host),
						Min(current.Bootstrapper, subscription.Position.Bootstrapper));
			}

			return minimum;
		}
	}

	private static LogReadPosition Min(LogReadPosition left, LogReadPosition right)
	{
		if (!left.HasFile || !right.HasFile)
		{
			return LogReadPosition.None;
		}

		return left.CompareTo(right) <= 0 ? left : right;
	}
}

public sealed record LogSubscription(LogQuery Query, LogCursor Position);
