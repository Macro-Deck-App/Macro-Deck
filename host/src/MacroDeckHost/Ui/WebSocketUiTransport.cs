using MacroDeckHost.Application.Ui.Transport;

namespace MacroDeckHost.Ui;

public sealed class WebSocketUiTransport : IUiTransport
{
	private readonly Dictionary<string, Connection> _connections = new(StringComparer.Ordinal);
	private readonly Dictionary<string, HashSet<string>> _groups = new(StringComparer.Ordinal);
	private readonly object _gate = new();

	public bool Add(string connectionId, Func<UiWebSocketEnvelope, CancellationToken, ValueTask<bool>> send)
	{
		lock (_gate)
		{
			return _connections.TryAdd(connectionId, new Connection(send));
		}
	}

	public void Remove(string connectionId)
	{
		lock (_gate)
		{
			if (!_connections.Remove(connectionId, out var connection))
			{
				return;
			}

			foreach (var groupName in connection.Groups)
			{
				if (_groups.TryGetValue(groupName, out var members))
				{
					members.Remove(connectionId);
					if (members.Count == 0)
					{
						_groups.Remove(groupName);
					}
				}
			}
		}
	}

	public Task Send<T>(T message, CancellationToken cancellationToken = default)
		where T : class
		=> Send(SnapshotAll(), message, cancellationToken);

	public Task SendToGroup<T>(string group, T message, CancellationToken cancellationToken = default)
		where T : class
		=> Send(SnapshotGroup(group), message, cancellationToken);

	public Task SendToConnection<T>(string connectionId, T message, CancellationToken cancellationToken = default)
		where T : class
		=> Send(SnapshotConnection(connectionId), message, cancellationToken);

	public Task SendNotification(string connectionId,
		string type,
		object payload,
		CancellationToken cancellationToken = default)
	{
		var connection = SnapshotConnection(connectionId).SingleOrDefault();
		if (connection is null)
		{
			return Task.CompletedTask;
		}

		return connection.Send(new UiWebSocketEnvelope(UiWebSocketProtocol.Version,
				"message",
				type,
				null,
				null,
				payload,
				null),
			cancellationToken).AsTask();
	}

	public Task AddToGroup(string connectionId, string group, CancellationToken cancellationToken = default)
	{
		if (cancellationToken.IsCancellationRequested)
		{
			return Task.FromCanceled(cancellationToken);
		}

		lock (_gate)
		{
			if (!_connections.TryGetValue(connectionId, out var connection))
			{
				return Task.CompletedTask;
			}

			if (!_groups.TryGetValue(group, out var members))
			{
				members = new HashSet<string>(StringComparer.Ordinal);
				_groups.Add(group, members);
			}

			members.Add(connectionId);
			connection.Groups.Add(group);
		}

		return Task.CompletedTask;
	}

	public Task RemoveFromGroup(string connectionId, string group, CancellationToken cancellationToken = default)
	{
		if (cancellationToken.IsCancellationRequested)
		{
			return Task.FromCanceled(cancellationToken);
		}

		lock (_gate)
		{
			if (_connections.TryGetValue(connectionId, out var connection))
			{
				connection.Groups.Remove(group);
			}

			if (_groups.TryGetValue(group, out var members))
			{
				members.Remove(connectionId);
				if (members.Count == 0)
				{
					_groups.Remove(group);
				}
			}
		}

		return Task.CompletedTask;
	}

	private static async Task Send<T>(IEnumerable<Connection> connections,
		T message,
		CancellationToken cancellationToken)
		where T : class
	{
		var envelope = new UiWebSocketEnvelope(UiWebSocketProtocol.Version,
			"message",
			typeof(T).Name,
			null,
			null,
			message,
			null);
		foreach (var connection in connections)
		{
			await connection.Send(envelope, cancellationToken);
		}
	}

	private Connection[] SnapshotAll()
	{
		lock (_gate)
		{
			return [.. _connections.Values];
		}
	}

	private Connection[] SnapshotGroup(string group)
	{
		lock (_gate)
		{
			return _groups.TryGetValue(group, out var members)
				? [.. members.Select(id => _connections.GetValueOrDefault(id)).OfType<Connection>()]
				: [];
		}
	}

	private Connection[] SnapshotConnection(string connectionId)
	{
		lock (_gate)
		{
			return _connections.TryGetValue(connectionId, out var connection) ? [connection] : [];
		}
	}

	private sealed class Connection(Func<UiWebSocketEnvelope, CancellationToken, ValueTask<bool>> send)
	{
		public Func<UiWebSocketEnvelope, CancellationToken, ValueTask<bool>> Send { get; } = send;
		public HashSet<string> Groups { get; } = new(StringComparer.Ordinal);
	}
}
