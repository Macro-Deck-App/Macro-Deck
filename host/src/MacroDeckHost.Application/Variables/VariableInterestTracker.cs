namespace MacroDeckHost.Application.Variables;

public sealed class VariableInterestTracker
{
	private readonly object _lock = new();
	private readonly Dictionary<string, HashSet<string>> _namesByConnection = new(StringComparer.Ordinal);

	public void Set(string connectionId, IReadOnlyCollection<string> names)
	{
		lock (_lock)
		{
			_namesByConnection[connectionId] = new HashSet<string>(names, StringComparer.Ordinal);
		}
	}

	public void RemoveConnection(string connectionId)
	{
		lock (_lock)
		{
			_namesByConnection.Remove(connectionId);
		}
	}

	public IReadOnlyList<(string ConnectionId, IReadOnlySet<string> Names)> Snapshot()
	{
		lock (_lock)
		{
			var snapshot = new List<(string, IReadOnlySet<string>)>(_namesByConnection.Count);
			foreach (var (connectionId, names) in _namesByConnection)
			{
				snapshot.Add((connectionId, new HashSet<string>(names, StringComparer.Ordinal)));
			}

			return snapshot;
		}
	}
}
